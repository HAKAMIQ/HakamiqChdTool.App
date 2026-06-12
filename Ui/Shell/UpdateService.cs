using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace HakamiqChdTool.App.Ui.Shell;

public static class UpdateService
{
    public static string? ConfiguredUpdateSource { get; set; }

    public static async Task CheckSilentlyAndOfferRestartAsync(Window ownerWindow, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ownerWindow);
        cancellationToken.ThrowIfCancellationRequested();

        UpdateManager? manager = null;

        try
        {
            manager = CreateUpdateManager();
            if (manager is null)
            {
                return;
            }

            UpdateManager updateManager = manager;

            UpdateInfo? info = await updateManager.CheckForUpdatesAsync().ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            if (info is null)
            {
                return;
            }

            await updateManager.DownloadUpdatesAsync(info, _ => { }, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();

            Dispatcher dispatcher = ownerWindow.Dispatcher;
            if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            DispatcherOperation operation = dispatcher.InvokeAsync(
                () =>
                {
                    string version = info.TargetFullRelease?.Version?.ToString()
                        ?? ArabicUi.Get("LocUpdate_NewVersionFallback");

                    var dialog = new ClearTaskLogConfirmationDialog(
                        ArabicUi.Get("LocUpdate_AvailableTitle"),
                        ArabicUi.Format("LocUpdate_AvailableRestartPrompt", version),
                        ArabicUi.Get("LocCommon_Ok"),
                        ArabicUi.Get("LocCommon_Cancel"))
                    {
                        Owner = ownerWindow
                    };

                    if (dialog.ShowDialog() == true)
                    {
                        updateManager.ApplyUpdatesAndRestart(info);
                    }
                },
                DispatcherPriority.Background,
                cancellationToken);

            await operation.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Log.Debug("Velopack update check cancelled.");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Velopack update check failed.");
        }
        finally
        {
            if (manager is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    private static UpdateManager? CreateUpdateManager()
    {
        if (TryNormalizeUpdateSource(ConfiguredUpdateSource, out string? configuredSource))
        {
            return new UpdateManager(configuredSource);
        }

        string? envFeed = Environment.GetEnvironmentVariable("HAKAMIQ_UPDATE_FEED");
        if (TryNormalizeUpdateSource(envFeed, out string? environmentSource))
        {
            return new UpdateManager(environmentSource);
        }

        string? githubRepo = Environment.GetEnvironmentVariable("HAKAMIQ_GITHUB_REPO");
        if (!TryBuildGitHubSource(githubRepo, out GithubSource? githubSource))
        {
            return null;
        }

        return new UpdateManager(githubSource);
    }

    private static bool TryNormalizeUpdateSource(
        string? source,
        [NotNullWhen(true)] out string? normalizedSource)
    {
        normalizedSource = null;

        if (string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        string trimmed = source.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri))
        {
            if (uri.IsFile)
            {
                return TryNormalizeLocalFeedDirectory(uri.LocalPath, out normalizedSource);
            }

            if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(uri.Host)
                && string.IsNullOrEmpty(uri.UserInfo))
            {
                normalizedSource = uri.AbsoluteUri;
                return true;
            }

            return false;
        }

        return TryNormalizeLocalFeedDirectory(trimmed, out normalizedSource);
    }

    private static bool TryNormalizeLocalFeedDirectory(
        string path,
        [NotNullWhen(true)] out string? normalizedPath)
    {
        normalizedPath = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());

            if (!Directory.Exists(fullPath)
                || IsUnsafeRoot(fullPath)
                || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
            {
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return false;
        }
    }

    private static bool TryBuildGitHubSource(
        string? repository,
        [NotNullWhen(true)] out GithubSource? source)
    {
        source = null;

        if (string.IsNullOrWhiteSpace(repository))
        {
            return false;
        }

        string trimmed = repository.Trim();
        int slash = trimmed.IndexOf('/');

        if (slash <= 0
            || slash >= trimmed.Length - 1
            || trimmed.IndexOf('/', slash + 1) >= 0)
        {
            return false;
        }

        string owner = trimmed[..slash];
        string repo = trimmed[(slash + 1)..];

        if (!IsSafeGitHubOwner(owner) || !IsSafeGitHubRepositoryName(repo))
        {
            return false;
        }

        source = new GithubSource($"https://github.com/{owner}/{repo}", string.Empty, false);
        return true;
    }

    private static bool IsSafeGitHubOwner(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 39
            || value[0] == '-'
            || value[^1] == '-')
        {
            return false;
        }

        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSafeGitHubRepositoryName(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 100
            || string.Equals(value, ".", StringComparison.Ordinal)
            || string.Equals(value, "..", StringComparison.Ordinal))
        {
            return false;
        }

        foreach (char character in value)
        {
            bool allowed =
                char.IsAsciiLetterOrDigit(character)
                || character is '-' or '_' or '.';

            if (!allowed)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string root = Path.GetFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsReparsePoint(current))
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnsafeRoot(string path)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            string? root = Path.GetPathRoot(fullPath);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return PathsEqual(fullPath, root);
        }
        catch (Exception ex) when (IsPathOrIoFailure(ex))
        {
            return true;
        }
    }

    private static string TrimDirectorySeparators(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static bool IsPathOrIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or System.Security.SecurityException;
    }
}
