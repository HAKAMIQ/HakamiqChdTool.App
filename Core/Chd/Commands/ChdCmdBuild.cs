using System.Globalization;
using System.IO;
using HakamiqChdTool.App.Core.Chd.Profiles;

namespace HakamiqChdTool.App.Core.Chd.Commands;

public static class ChdmanCommandBuilder
{
    public const string DvdSectorAlignmentMessageKey = "LocChdPolicy_DvdSectorAlignmentInvalid";

    public static IReadOnlyList<string> BuildCreateArgs(
        ChdPlatformProfile profile,
        string inputPath,
        string outputPath,
        int? processorCount)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var args = new List<string>
        {
            ChdPlatformProfiles.ToCommandName(profile.CommandKind),
            "-i",
            inputPath,
            "-o",
            outputPath
        };

        if (profile.HunkSize is int hunkSize)
        {
            args.Add("-hs");
            args.Add(hunkSize.ToString(CultureInfo.InvariantCulture));
        }

        if (processorCount is > 0)
        {
            args.Add("-np");
            args.Add(processorCount.Value.ToString(CultureInfo.InvariantCulture));
        }

        return args;
    }

    public static void ValidateDvdSectorAlignment(string inputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        long length = new FileInfo(inputPath).Length;
        if (length % 2048 != 0)
        {
            throw new InvalidOperationException(DvdSectorAlignmentMessageKey);
        }
    }
}
