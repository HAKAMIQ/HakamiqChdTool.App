using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3DiscSfbReader
{
    private const int MaxDiscSfbBytes = 256 * 1024;

    private static readonly Regex TitleIdRegex = new(
        @"\b[A-Z]{4}\d{5}\b",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public string? ReadDiscIdFromFile(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);

            if (!File.Exists(fullPath) || IsReparsePoint(fullPath))
            {
                return null;
            }

            using FileStream stream = File.Open(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            return ReadDiscId(stream);
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return null;
        }
    }

    public string? ReadDiscId(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            int length = stream.CanSeek
                ? (int)Math.Min(MaxDiscSfbBytes, Math.Max(0, stream.Length))
                : MaxDiscSfbBytes;

            if (length <= 0)
            {
                return null;
            }

            byte[] buffer = new byte[length];
            int read = ReadBounded(stream, buffer);
            if (read <= 0)
            {
                return null;
            }

            string text = BytesToSearchableAscii(buffer.AsSpan(0, read));
            Match match = TitleIdRegex.Match(text);
            return match.Success ? match.Value : null;
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return null;
        }
    }

    private static int ReadBounded(Stream stream, byte[] buffer)
    {
        int total = 0;

        while (total < buffer.Length)
        {
            int read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static string BytesToSearchableAscii(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);

        foreach (byte value in bytes)
        {
            builder.Append(value is >= 32 and <= 126 ? (char)value : ' ');
        }

        return builder.ToString();
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return true;
        }
    }

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidDataException
        or RegexMatchTimeoutException
        or System.Security.SecurityException;
}