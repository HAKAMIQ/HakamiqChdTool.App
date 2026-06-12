using System;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services.ConsoleMedia;

internal sealed class ConsoleDiscScanContext
{
    private const int MaxProbeBytes = 16 * 1024 * 1024;
    private const int ReadBufferSize = 1024 * 1024;

    private ConsoleDiscScanContext(
        string path,
        string searchableText,
        string searchablePathText)
    {
        Path = path;
        SearchableText = searchableText;
        SearchablePathText = searchablePathText;
    }

    public string Path { get; }

    public string SearchableText { get; }

    public string SearchablePathText { get; }

    public static bool TryCreate(string path, out ConsoleDiscScanContext context)
    {
        context = new ConsoleDiscScanContext(string.Empty, string.Empty, string.Empty);

        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }

        if (!File.Exists(fullPath))
        {
            return false;
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                ReadBufferSize,
                FileOptions.SequentialScan);

            long remaining = Math.Min(stream.Length, MaxProbeBytes);
            if (remaining <= 0)
            {
                return false;
            }

            byte[] buffer = new byte[ReadBufferSize];
            var builder = new StringBuilder((int)Math.Min(remaining, MaxProbeBytes));

            while (remaining > 0)
            {
                int targetRead = (int)Math.Min(buffer.Length, remaining);
                int read = stream.Read(buffer, 0, targetRead);
                if (read <= 0)
                {
                    break;
                }

                remaining -= read;
                AppendSearchableAscii(builder, buffer.AsSpan(0, read));
            }

            context = new ConsoleDiscScanContext(
                fullPath,
                builder.ToString(),
                BuildSearchablePathText(fullPath));

            return context.SearchableText.Length > 0
                || context.SearchablePathText.Length > 0;
        }
        catch (Exception ex) when (IsExpectedReadException(ex) || IsExpectedPathException(ex))
        {
            return false;
        }
    }

    public bool ContainsText(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && SearchableText.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    public bool ContainsPathHint(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && SearchablePathText.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;

    private static string BuildSearchablePathText(string fullPath)
    {
        string directory = System.IO.Path.GetDirectoryName(fullPath) ?? string.Empty;
        string name = System.IO.Path.GetFileNameWithoutExtension(fullPath);
        return $"{directory} {name}".Replace('_', ' ').Replace('-', ' ');
    }

    private static void AppendSearchableAscii(StringBuilder builder, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes)
        {
            builder.Append(value is >= 32 and <= 126 ? (char)value : ' ');
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or InvalidDataException;
}
