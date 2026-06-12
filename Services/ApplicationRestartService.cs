using Serilog;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace HakamiqChdTool.App.Services;

public sealed class ApplicationRestartContext
{
    public const string OptionsWindowName = "Options";

    public string ReopenWindow { get; set; } = string.Empty;

    public string OptionsTabKey { get; set; } = string.Empty;

    public double MainWindowLeft { get; set; }

    public double MainWindowTop { get; set; }

    public double MainWindowWidth { get; set; }

    public double MainWindowHeight { get; set; }

    public WindowState MainWindowState { get; set; } = WindowState.Normal;

    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public static class ApplicationRestartService
{
    private const string RestartContextFileName = "restart-context.json";
    private const long MaximumRestartContextBytes = 64 * 1024;

    private static readonly TimeSpan RestartContextMaxAge = TimeSpan.FromMinutes(10);
    private static readonly ILogger Logger = Log.ForContext(typeof(ApplicationRestartService));

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static ApplicationRestartContext CreateRestartContext(
        Window mainWindow,
        string reopenWindow,
        string? optionsTabKey)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);

        Rect bounds = mainWindow.WindowState == WindowState.Normal
            ? new Rect(mainWindow.Left, mainWindow.Top, mainWindow.Width, mainWindow.Height)
            : mainWindow.RestoreBounds;

        return new ApplicationRestartContext
        {
            ReopenWindow = NormalizeContextValue(reopenWindow, maxLength: 64),
            OptionsTabKey = NormalizeContextValue(optionsTabKey, maxLength: 128),
            MainWindowLeft = bounds.Left,
            MainWindowTop = bounds.Top,
            MainWindowWidth = bounds.Width,
            MainWindowHeight = bounds.Height,
            MainWindowState = mainWindow.WindowState == WindowState.Minimized ? WindowState.Normal : mainWindow.WindowState,
            CreatedUtc = DateTimeOffset.UtcNow
        };
    }

    public static bool TryRestartCurrentApplication(ApplicationRestartContext restartContext)
    {
        ArgumentNullException.ThrowIfNull(restartContext);

        if (!TrySaveRestartContext(restartContext))
        {
            return false;
        }

        bool restarted = TryRestartCurrentApplication();
        if (!restarted)
        {
            TryDeleteRestartContext();
        }

        return restarted;
    }

    public static bool TryRestartCurrentApplication()
    {
        string? processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = processPath,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppContext.BaseDirectory
            };

            using Process? startedProcess = Process.Start(startInfo);
            if (startedProcess is null)
            {
                return false;
            }

            RequestApplicationShutdown();
            return true;
        }
        catch (Exception ex) when (IsRestartFailure(ex))
        {
            Logger.Warning(ex, "Application restart request failed.");
            return false;
        }
    }

    public static ApplicationRestartContext? ConsumeRestartContext()
    {
        try
        {
            string path = GetRestartContextPath();

            if (!File.Exists(path))
            {
                return null;
            }

            FileInfo contextFile = new(path);
            if (!contextFile.Exists
                || contextFile.Length <= 0
                || contextFile.Length > MaximumRestartContextBytes
                || IsReparsePoint(path))
            {
                TryDeleteRestartContext();
                return null;
            }

            string json = File.ReadAllText(path);
            TryDeleteRestartContext();

            ApplicationRestartContext? context = JsonSerializer.Deserialize<ApplicationRestartContext>(json, JsonOptions);
            if (context is null || !IsRestartContextFresh(context))
            {
                return null;
            }

            NormalizeConsumedContext(context);
            return context;
        }
        catch (Exception ex) when (IsRestartFailure(ex) || ex is JsonException)
        {
            Logger.Debug(ex, "Restart context could not be consumed.");
            TryDeleteRestartContext();
            return null;
        }
    }

    public static bool TryRestoreMainWindowBounds(Window mainWindow, ApplicationRestartContext context)
    {
        ArgumentNullException.ThrowIfNull(mainWindow);
        ArgumentNullException.ThrowIfNull(context);

        if (!IsFinite(context.MainWindowLeft)
            || !IsFinite(context.MainWindowTop)
            || !IsFinite(context.MainWindowWidth)
            || !IsFinite(context.MainWindowHeight)
            || context.MainWindowWidth <= 0
            || context.MainWindowHeight <= 0)
        {
            return false;
        }

        WindowState restoreState = context.MainWindowState == WindowState.Maximized
            ? WindowState.Maximized
            : WindowState.Normal;

        mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
        mainWindow.WindowState = WindowState.Normal;
        mainWindow.Left = context.MainWindowLeft;
        mainWindow.Top = context.MainWindowTop;
        mainWindow.Width = context.MainWindowWidth;
        mainWindow.Height = context.MainWindowHeight;

        if (restoreState == WindowState.Maximized)
        {
            mainWindow.WindowState = WindowState.Maximized;
        }

        return true;
    }

    private static bool TrySaveRestartContext(ApplicationRestartContext restartContext)
    {
        try
        {
            string path = GetRestartContextPath();
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrWhiteSpace(directory))
            {
                return false;
            }

            Directory.CreateDirectory(directory);

            if (IsReparsePoint(directory))
            {
                return false;
            }

            restartContext.CreatedUtc = DateTimeOffset.UtcNow;
            restartContext.ReopenWindow = NormalizeContextValue(restartContext.ReopenWindow, maxLength: 64);
            restartContext.OptionsTabKey = NormalizeContextValue(restartContext.OptionsTabKey, maxLength: 128);

            string json = JsonSerializer.Serialize(restartContext, JsonOptions);
            WriteTextAtomically(path, json, directory);

            return true;
        }
        catch (Exception ex) when (IsRestartFailure(ex) || ex is JsonException)
        {
            Logger.Warning(ex, "Restart context could not be saved.");
            return false;
        }
    }

    private static bool IsRestartContextFresh(ApplicationRestartContext context)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (context.CreatedUtc > now)
        {
            return false;
        }

        return now - context.CreatedUtc <= RestartContextMaxAge;
    }

    private static void TryDeleteRestartContext()
    {
        try
        {
            string path = GetRestartContextPath();
            if (File.Exists(path) && !IsReparsePoint(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (IsRestartFailure(ex))
        {
            Logger.Debug(ex, "Restart context could not be deleted.");
        }
    }

    private static string GetRestartContextPath()
    {
        return Path.Combine(AppPaths.LocalAppRoot, RestartContextFileName);
    }

    private static void WriteTextAtomically(
        string targetPath,
        string content,
        string directory)
    {
        string fullDirectory = Path.GetFullPath(directory);
        string fullTargetPath = Path.GetFullPath(targetPath);

        if (!IsPathInsideDirectory(fullTargetPath, fullDirectory))
        {
            throw new IOException();
        }

        if (File.Exists(fullTargetPath) && IsReparsePoint(fullTargetPath))
        {
            throw new IOException();
        }

        string tempPath = Path.Combine(
            fullDirectory,
            $"{Path.GetFileName(fullTargetPath)}.{Guid.NewGuid():N}.tmp");

        if (!IsPathInsideDirectory(tempPath, fullDirectory))
        {
            throw new IOException();
        }

        try
        {
            byte[] bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
                .GetBytes(content);

            using (FileStream stream = new(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 16 * 1024,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
            }

            if (IsReparsePoint(tempPath))
            {
                throw new IOException();
            }

            File.Move(tempPath, fullTargetPath, overwrite: true);
        }
        finally
        {
            TryDeleteFile(tempPath);
        }
    }

    private static void RequestApplicationShutdown()
    {
        System.Windows.Application? application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        try
        {
            if (application.Dispatcher.HasShutdownStarted || application.Dispatcher.HasShutdownFinished)
            {
                return;
            }

            if (application.Dispatcher.CheckAccess())
            {
                application.Shutdown();
                return;
            }

            _ = application.Dispatcher.BeginInvoke(new Action(application.Shutdown));
        }
        catch (InvalidOperationException ex)
        {
            Logger.Debug(ex, "Application shutdown request could not be dispatched.");
        }
    }

    private static void NormalizeConsumedContext(ApplicationRestartContext context)
    {
        context.ReopenWindow = NormalizeContextValue(context.ReopenWindow, maxLength: 64);
        context.OptionsTabKey = NormalizeContextValue(context.OptionsTabKey, maxLength: 128);

        if (context.MainWindowState is not WindowState.Normal and not WindowState.Maximized)
        {
            context.MainWindowState = WindowState.Normal;
        }
    }

    private static string NormalizeContextValue(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }

    private static bool IsPathInsideDirectory(string fullPath, string directory)
    {
        string normalizedDirectory = Path.GetFullPath(directory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        string normalizedPath = Path.GetFullPath(fullPath);

        return normalizedPath.StartsWith(normalizedDirectory, StringComparison.OrdinalIgnoreCase);
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
        catch (Exception ex) when (IsRestartFailure(ex))
        {
            return true;
        }
    }

    private static void TryDeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath) && !IsReparsePoint(filePath))
            {
                File.Delete(filePath);
            }
        }
        catch (Exception ex) when (IsRestartFailure(ex))
        {
            Logger.Debug(ex, "Temporary restart context file could not be deleted.");
        }
    }

    private static bool IsRestartFailure(Exception ex)
    {
        return ex is InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or Win32Exception
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}