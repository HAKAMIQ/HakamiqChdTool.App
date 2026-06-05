using HakamiqChdTool.App.Models.PlayStation;
using System.IO;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3ContentClassifier
{
    public PS3InputFormat Classify(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PS3InputFormat.Unknown;
        }

        try
        {
            if (Directory.Exists(path))
            {
                return PS3InputFormat.Folder;
            }

            if (!File.Exists(path))
            {
                return PS3InputFormat.Unknown;
            }

            return Path.GetExtension(path).ToLowerInvariant() switch
            {
                ".iso" => PS3InputFormat.Iso,
                ".pkg" => PS3InputFormat.Pkg,
                ".chd" => PS3InputFormat.Chd,
                _ => PS3InputFormat.Unknown
            };
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return PS3InputFormat.Unknown;
        }
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or IOException;
}
