using HakamiqChdTool.App.Core.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public sealed record EcmSetCheck(bool IsValid, string MessageKey, string? CuePath, long DecodedBytes)
{
    public static EcmSetCheck Fail(string messageKey) => new(false, messageKey, null, 0);
}

/// <summary>
/// Prepares an ECM image for chdman createcd. With a CUE next to it, the CUE is copied
/// into the temp workspace and every track it names is placed beside it: .ecm tracks are
/// decoded, plain tracks are hard-linked or copied. Without a CUE, a single data track is
/// assumed only when the decoded image starts with a Mode 1 or Mode 2 sector. Nothing is
/// written to the source folder.
/// </summary>
public static class EcmSetPreparer
{
    public const string UnsupportedNameMessageKey = "LocWorkflow_EcmUnsupportedName";
    public const string CueIncompleteMessageKey = "LocWorkflow_EcmCueIncomplete";
    public const string NeedsCueMessageKey = "LocWorkflow_EcmNeedsCue";
    public const string PreparedCueName = "tmp.cue";
    public const string StandaloneImageName = "tmp.bin";

    private const long FreeSpaceReserveBytes = 64L * 1024L * 1024L;

    public static EcmSetCheck Check(string ecmPath)
    {
        if (!EcmEvidence.TryGetDecodedName(ecmPath, out _))
        {
            return EcmSetCheck.Fail(UnsupportedNameMessageKey);
        }

        string? cue = EcmEvidence.FindReferencingCue(ecmPath);
        if (cue is null)
        {
            EcmScanResult scan = EcmDecoder.Scan(ecmPath);
            if (!scan.IsValid)
            {
                return EcmSetCheck.Fail(scan.MessageKey);
            }

            return scan.OutputBytes % EcmDecoder.SectorBytes == 0
                ? new EcmSetCheck(true, string.Empty, null, scan.OutputBytes)
                : EcmSetCheck.Fail(NeedsCueMessageKey);
        }

        EcmCueSet? set = EcmEvidence.TryReadCueSet(cue);
        if (set is null)
        {
            return EcmSetCheck.Fail(CueIncompleteMessageKey);
        }

        long total = 0;
        foreach (EcmCueTrackFile file in set.Files)
        {
            if (file.EcmPath is null)
            {
                total += new FileInfo(file.PlainPath!).Length;
                continue;
            }

            EcmScanResult scan = EcmDecoder.Scan(file.EcmPath);
            if (!scan.IsValid)
            {
                return EcmSetCheck.Fail(scan.MessageKey);
            }

            total += scan.OutputBytes;
        }

        return new EcmSetCheck(true, string.Empty, set.CuePath, total);
    }

    /// <summary>
    /// Fills <paramref name="workspaceDirectory"/> and returns the prepared CUE path, or the
    /// failure message key.
    /// </summary>
    public static async Task<(string? PreparedCuePath, string? FailureKey)> PrepareAsync(
        string ecmPath,
        string workspaceDirectory,
        CancellationToken cancellationToken)
    {
        EcmSetCheck check = Check(ecmPath);
        if (!check.IsValid)
        {
            return (null, check.MessageKey);
        }

        string? root = Path.GetPathRoot(Path.GetFullPath(workspaceDirectory));
        if (string.IsNullOrWhiteSpace(root)
            || new DriveInfo(root).AvailableFreeSpace < check.DecodedBytes + FreeSpaceReserveBytes)
        {
            return (null, EcmDecoder.StagingFailedMessageKey);
        }

        string preparedCue = Path.Combine(workspaceDirectory, PreparedCueName);
        if (check.CuePath is null)
        {
            string image = Path.Combine(workspaceDirectory, StandaloneImageName);
            string? failure = await EcmDecoder.DecodeToFileAsync(ecmPath, image, cancellationToken).ConfigureAwait(false);
            if (failure is not null)
            {
                return (null, failure);
            }

            string? mode = ReadDataTrackMode(image);
            if (mode is null)
            {
                return (null, NeedsCueMessageKey);
            }

            await File.WriteAllTextAsync(
                    preparedCue,
                    "FILE \"" + StandaloneImageName + "\" BINARY\r\n  TRACK 01 " + mode + "\r\n    INDEX 01 00:00:00\r\n",
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    cancellationToken)
                .ConfigureAwait(false);
            return (preparedCue, null);
        }

        EcmCueSet set = EcmEvidence.TryReadCueSet(check.CuePath)!;
        var staged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (EcmCueTrackFile file in set.Files)
        {
            if (!staged.Add(file.FileName))
            {
                continue;
            }

            if (string.Equals(file.FileName, PreparedCueName, StringComparison.OrdinalIgnoreCase))
            {
                return (null, CueIncompleteMessageKey);
            }

            string target = Path.Combine(workspaceDirectory, file.FileName);
            if (file.EcmPath is not null)
            {
                string? failure = await EcmDecoder.DecodeToFileAsync(file.EcmPath, target, cancellationToken).ConfigureAwait(false);
                if (failure is not null)
                {
                    return (null, failure);
                }
            }
            else if (!await CloneCdImageStager.TryStageAsync(file.PlainPath!, target, cancellationToken).ConfigureAwait(false))
            {
                return (null, EcmDecoder.StagingFailedMessageKey);
            }
        }

        // The CUE is copied unchanged: its FILE names are plain names that now sit next to it.
        File.Copy(set.CuePath, preparedCue, overwrite: false);
        return (preparedCue, null);
    }

    /// <summary>MODE1/2352 or MODE2/2352 when the image starts with a CD sync pattern and a data mode.</summary>
    internal static string? ReadDataTrackMode(string imagePath)
    {
        Span<byte> header = stackalloc byte[16];
        using (var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (stream.Length % EcmDecoder.SectorBytes != 0
                || stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) != header.Length)
            {
                return null;
            }
        }

        bool sync = header[0] == 0 && header[11] == 0 && header[1..11].IndexOfAnyExcept((byte)0xFF) < 0;
        return !sync
            ? null
            : header[15] switch
            {
                1 => "MODE1/2352",
                2 => "MODE2/2352",
                _ => null
            };
    }
}
