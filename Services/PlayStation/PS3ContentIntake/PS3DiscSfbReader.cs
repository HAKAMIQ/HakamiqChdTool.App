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
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            using FileStream stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
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

            byte[] buffer = new byte[length];
            int read = stream.Read(buffer, 0, buffer.Length);
            string text = BytesToSearchableAscii(buffer.AsSpan(0, read));
            Match match = TitleIdRegex.Match(text);
            return match.Success ? match.Value : null;
        }
        catch (Exception ex) when (IsExpectedReadException(ex))
        {
            return null;
        }
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

    private static bool IsExpectedReadException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidDataException;
}
