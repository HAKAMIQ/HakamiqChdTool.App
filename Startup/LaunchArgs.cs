using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Startup;

/// <summary>The operation a launch asks for. <see cref="None"/> keeps the mode selected in the window.</summary>
internal enum LaunchMode
{
    None = 0,
    Convert = 1,
    Extract = 2,
    Verify = 3
}

/// <summary>
/// Files and folders handed to the app by Explorer or a command line, with the mode to use.
/// <paramref name="Start"/> starts processing once they are added; <paramref name="ExitWhenDone"/> closes
/// the app after that run, with an exit code (see <see cref="LaunchArgs.ExitCodeFor"/>). <paramref name="Quick"/> comes
/// from the Explorer menu: the first instance may show the small quick window instead of the main window. It is never
/// forwarded to a running instance, which adds the files to its queue as before.
/// </summary>
internal sealed record LaunchRequest(LaunchMode Mode, IReadOnlyList<string> Paths, bool Start = false, bool ExitWhenDone = false, bool Quick = false)
{
    public static LaunchRequest Empty { get; } = new(LaunchMode.None, []);

    public bool HasPaths => Paths.Count > 0;
}

/// <summary>
/// Parses <c>[--quick] [--mode convert|extract|verify] [--start] [--exit-when-done] [--add &lt;path&gt;...]</c>. Paths are made absolute against the
/// current directory of the launching process, because a running instance that receives them has its own.
/// Paths are still untrusted: queue intake validates them like any added file.
/// </summary>
internal static class LaunchArgs
{
    public const int MaxPaths = 1024;
    public const int MaxPathLength = 32_767;

    public const int ExitOk = 0;
    public const int ExitFailures = 1;
    public const int ExitStopped = 2;
    public const int ExitNothingToDo = 3;

    public const string Usage =
        "HakamiqChdTool.exe [--mode convert|extract|verify] [--start] [--exit-when-done] --add <file or folder>...";

    /// <summary>The usage as short lines, each in a left-to-right embedding, so it reads correctly inside Arabic
    /// text. The lines are short enough not to wrap; a wrapped embedding would be reordered.</summary>
    public static string FormatUsageForDialog(string error)
    {
        string[] lines =
        [
            error,
            string.Empty,
            "HakamiqChdTool.exe",
            "  --mode convert|extract|verify",
            "  --start",
            "  --exit-when-done",
            "  --add <file or folder>...",
            string.Empty,
            "exit: 0 done, 1 failures, 2 stopped, 3 nothing to process"
        ];
        return string.Join(Environment.NewLine, lines.Select(line => line.Length == 0 ? line : "\u202A" + line + "\u202C"));
    }

    /// <summary>0: every item finished or was skipped; 1: at least one failed; 2: the run was stopped.</summary>
    public static int ExitCodeFor(bool wasCancelled, int failed) =>
        wasCancelled ? ExitStopped : failed > 0 ? ExitFailures : ExitOk;

    public static bool TryParse(IReadOnlyList<string> args, out LaunchRequest request, out string? error)
    {
        ArgumentNullException.ThrowIfNull(args);
        request = LaunchRequest.Empty;
        error = null;

        LaunchMode mode = LaunchMode.None;
        bool start = false;
        bool exitWhenDone = false;
        bool quick = false;
        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool readingPaths = false;

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i] ?? string.Empty;

            if (string.Equals(arg, "--mode", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 >= args.Count || !TryParseMode(args[i + 1], out mode))
                {
                    error = "--mode needs convert, extract or verify.";
                    return false;
                }

                i++;
                readingPaths = false;
                continue;
            }

            if (string.Equals(arg, "--add", StringComparison.OrdinalIgnoreCase))
            {
                readingPaths = true;
                continue;
            }

            if (string.Equals(arg, "--start", StringComparison.OrdinalIgnoreCase))
            {
                start = true;
                readingPaths = false;
                continue;
            }

            if (string.Equals(arg, "--exit-when-done", StringComparison.OrdinalIgnoreCase))
            {
                exitWhenDone = true;
                readingPaths = false;
                continue;
            }

            if (string.Equals(arg, "--quick", StringComparison.OrdinalIgnoreCase))
            {
                quick = true;
                readingPaths = false;
                continue;
            }

            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                error = $"Unknown option: {arg}";
                return false;
            }

            if (!readingPaths)
            {
                error = $"Unexpected argument: {arg}";
                return false;
            }

            if (!TryNormalizePath(arg, out string? fullPath))
            {
                error = $"Invalid path: {arg}";
                return false;
            }

            if (seen.Add(fullPath))
            {
                if (paths.Count >= MaxPaths)
                {
                    error = $"Too many paths; the limit is {MaxPaths}.";
                    return false;
                }

                paths.Add(fullPath);
            }
        }

        if (exitWhenDone && !start)
        {
            error = "--exit-when-done needs --start.";
            return false;
        }

        if (start && paths.Count == 0)
        {
            error = "--start needs files or folders after --add.";
            return false;
        }

        if (quick && (mode == LaunchMode.None || paths.Count == 0))
        {
            error = "--quick needs --mode and files or folders after --add.";
            return false;
        }

        if (quick && (start || exitWhenDone))
        {
            error = "--quick cannot be combined with --start or --exit-when-done.";
            return false;
        }

        request = new LaunchRequest(mode, paths, start, exitWhenDone, quick);
        return true;
    }

    public static bool TryParseMode(string? value, out LaunchMode mode)
    {
        mode = (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "convert" => LaunchMode.Convert,
            "extract" => LaunchMode.Extract,
            "verify" => LaunchMode.Verify,
            _ => LaunchMode.None
        };

        return mode != LaunchMode.None;
    }

    public static string ModeName(LaunchMode mode) => mode switch
    {
        LaunchMode.Convert => "convert",
        LaunchMode.Extract => "extract",
        LaunchMode.Verify => "verify",
        _ => string.Empty
    };

    /// <summary>Accepts only a non-empty absolute path (after resolving), without control characters or wildcards.</summary>
    public static bool TryNormalizePath(string? value, out string fullPath)
    {
        fullPath = string.Empty;
        string trimmed = (value ?? string.Empty).Trim().Trim('"');
        if (trimmed.Length == 0 || trimmed.Length > MaxPathLength)
        {
            return false;
        }

        foreach (char c in trimmed)
        {
            if (char.IsControl(c) || c == '*' || c == '?')
            {
                return false;
            }
        }

        try
        {
            string candidate = Path.GetFullPath(trimmed);
            if (!Path.IsPathFullyQualified(candidate) || candidate.Length > MaxPathLength)
            {
                return false;
            }

            fullPath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException or System.Security.SecurityException)
        {
            return false;
        }
    }
}
