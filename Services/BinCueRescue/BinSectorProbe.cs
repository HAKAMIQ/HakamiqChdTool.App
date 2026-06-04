using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services.BinCueRescue;

internal static class BinSectorProbe
{
    private const int RawSectorSize = 2352;
    private const int CookedSectorSize = 2048;
    private const int ModeByteOffset = 15;
    private const int IsoPrimaryVolumeDescriptorSector = 16;
    private const int MinimumUsefulSampleCount = 2;

    private static readonly byte[] SyncPattern =
    [
        0x00,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0xFF,
        0x00
    ];

    public static BinSectorProbeResult Probe(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string fullPath;
        try
        {
            fullPath = NormalizeFullPath(path);
        }
        catch (Exception ex) when (IsPathFailure(ex))
        {
            return CreateResult(
                path,
                0,
                null,
                BinTrackKind.Unknown,
                null,
                0,
                BinSectorProbeReasonCode.FileDoesNotExist);
        }

        FileInfo file = new(fullPath);
        if (!TryGetSafeFileLength(file, out long length, out BinSectorProbeReasonCode refusalReason))
        {
            return CreateResult(
                fullPath,
                length,
                null,
                BinTrackKind.Unknown,
                null,
                0,
                refusalReason);
        }

        try
        {
            using FileStream stream = new(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: RawSectorSize,
                FileOptions.SequentialScan);

            if (length % RawSectorSize == 0)
            {
                return ProbeRaw2352(fullPath, length, stream);
            }

            if (length % CookedSectorSize == 0)
            {
                return ProbeCooked2048(fullPath, length, stream);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return CreateResult(
                fullPath,
                length,
                null,
                BinTrackKind.Unknown,
                null,
                0,
                BinSectorProbeReasonCode.InsufficientRaw2352Evidence);
        }

        return CreateResult(
            fullPath,
            length,
            null,
            BinTrackKind.NonStandard,
            null,
            0,
            BinSectorProbeReasonCode.LengthNotDivisibleBySupportedSectorSize);
    }

    private static bool TryGetSafeFileLength(
        FileInfo file,
        out long length,
        out BinSectorProbeReasonCode refusalReason)
    {
        length = 0;
        refusalReason = BinSectorProbeReasonCode.FileDoesNotExist;

        try
        {
            if (!file.Exists)
            {
                return false;
            }

            if (HasReparsePointInExistingPathFromVolumeRoot(file.FullName))
            {
                refusalReason = BinSectorProbeReasonCode.InsufficientRaw2352Evidence;
                return false;
            }

            length = file.Length;
            if (length <= 0)
            {
                refusalReason = BinSectorProbeReasonCode.FileIsEmpty;
                return false;
            }

            return true;
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            length = 0;
            refusalReason = BinSectorProbeReasonCode.InsufficientRaw2352Evidence;
            return false;
        }
    }

    private static BinSectorProbeResult ProbeRaw2352(string path, long length, FileStream stream)
    {
        List<BinSectorProbeReasonCode> reasons = [];
        long[] offsets = BuildSampleOffsets(length, RawSectorSize);

        int mode1Count = 0;
        int mode2Count = 0;
        int zeroModeCount = 0;
        int syncCount = 0;
        byte? firstModeByte = null;

        foreach (long offset in offsets)
        {
            byte[] sector = ReadSector(stream, offset, RawSectorSize);
            if (sector.Length < ModeByteOffset + 1)
            {
                continue;
            }

            if (!HasSyncPattern(sector))
            {
                continue;
            }

            syncCount++;
            byte modeByte = sector[ModeByteOffset];
            firstModeByte ??= modeByte;

            if (modeByte == 0x01)
            {
                mode1Count++;
            }
            else if (modeByte == 0x02)
            {
                mode2Count++;
            }
            else if (modeByte == 0x00)
            {
                zeroModeCount++;
            }
            else
            {
                reasons.Add(BinSectorProbeReasonCode.UnexpectedRawModeByteObserved);
            }
        }

        if (mode1Count > 0 && mode2Count > 0)
        {
            reasons.Add(BinSectorProbeReasonCode.MixedRawDataModeBytesObserved);
            return CreateResult(path, length, RawSectorSize, BinTrackKind.NonStandard, firstModeByte, syncCount, reasons);
        }

        if (mode1Count > 0)
        {
            reasons.Add(BinSectorProbeReasonCode.Raw2352Mode1Observed);
            return CreateResult(path, length, RawSectorSize, BinTrackKind.Raw2352Mode1, 0x01, syncCount, reasons);
        }

        if (mode2Count > 0)
        {
            reasons.Add(BinSectorProbeReasonCode.Raw2352Mode2Observed);
            return CreateResult(path, length, RawSectorSize, BinTrackKind.Raw2352Mode2, 0x02, syncCount, reasons);
        }

        if (zeroModeCount > 0)
        {
            reasons.Add(BinSectorProbeReasonCode.OnlyZeroModeRawSyncObserved);
            return CreateResult(path, length, RawSectorSize, BinTrackKind.Unknown, 0x00, syncCount, reasons);
        }

        if (LooksLikeAudioCandidate(length, offsets.Length, syncCount))
        {
            reasons.Add(BinSectorProbeReasonCode.Raw2352AudioCandidate);
            return CreateResult(path, length, RawSectorSize, BinTrackKind.Raw2352AudioCandidate, null, syncCount, reasons);
        }

        reasons.Add(BinSectorProbeReasonCode.InsufficientRaw2352Evidence);
        return CreateResult(path, length, RawSectorSize, BinTrackKind.Unknown, null, syncCount, reasons);
    }

    private static BinSectorProbeResult ProbeCooked2048(string path, long length, FileStream stream)
    {
        List<BinSectorProbeReasonCode> reasons = [];

        long pvdOffset = (long)IsoPrimaryVolumeDescriptorSector * CookedSectorSize;
        if (pvdOffset + 6 <= length)
        {
            byte[] pvd = ReadBytes(stream, pvdOffset, 6);
            if (pvd.Length >= 6 && pvd[0] == 0x01 && IsCd001(pvd, 1))
            {
                reasons.Add(BinSectorProbeReasonCode.Iso9660PrimaryVolumeDescriptorObserved);
                return CreateResult(path, length, CookedSectorSize, BinTrackKind.Cooked2048Data, null, 0, reasons);
            }
        }

        reasons.Add(BinSectorProbeReasonCode.Cooked2048WithoutConfirmedIso9660Pvd);
        return CreateResult(path, length, CookedSectorSize, BinTrackKind.Cooked2048Data, null, 0, reasons);
    }

    private static bool LooksLikeAudioCandidate(long length, int sampleCount, int syncCount)
    {
        return length >= RawSectorSize
               && sampleCount >= MinimumUsefulSampleCount
               && syncCount == 0;
    }

    private static long[] BuildSampleOffsets(long length, int sectorSize)
    {
        long sectorCount = length / sectorSize;
        if (sectorCount <= 0)
        {
            return [];
        }

        long middleSector = Math.Max(0, sectorCount / 2);
        long lastSector = Math.Max(0, sectorCount - 1);

        long[] requestedSectors =
        [
            0,
            1,
            4,
            16,
            middleSector,
            lastSector
        ];

        return
        [
            .. requestedSectors
                .Where(sector => sector >= 0 && sector < sectorCount)
                .Distinct()
                .Select(sector => sector * sectorSize)
                .Order()
        ];
    }

    private static byte[] ReadSector(FileStream stream, long offset, int sectorSize)
    {
        return ReadBytes(stream, offset, sectorSize);
    }

    private static byte[] ReadBytes(FileStream stream, long offset, int count)
    {
        if (offset < 0 || offset >= stream.Length || count <= 0)
        {
            return [];
        }

        int safeCount = (int)Math.Min(count, stream.Length - offset);
        byte[] buffer = new byte[safeCount];

        stream.Seek(offset, SeekOrigin.Begin);
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int read = stream.Read(buffer, totalRead, buffer.Length - totalRead);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
        }

        if (totalRead == buffer.Length)
        {
            return buffer;
        }

        Array.Resize(ref buffer, totalRead);
        return buffer;
    }

    private static bool HasSyncPattern(byte[] sector)
    {
        if (sector.Length < SyncPattern.Length)
        {
            return false;
        }

        for (int i = 0; i < SyncPattern.Length; i++)
        {
            if (sector[i] != SyncPattern[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsCd001(byte[] buffer, int offset)
    {
        return buffer.Length >= offset + 5
               && buffer[offset] == (byte)'C'
               && buffer[offset + 1] == (byte)'D'
               && buffer[offset + 2] == (byte)'0'
               && buffer[offset + 3] == (byte)'0'
               && buffer[offset + 4] == (byte)'1';
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            return HasReparsePointInExistingPath(candidate, root);
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool HasReparsePointInExistingPath(string candidatePath, string rootPath)
    {
        try
        {
            string candidate = NormalizeFullPath(candidatePath);
            string root = NormalizeFullPath(rootPath);

            if (!IsSamePathOrChild(candidate, root))
            {
                return true;
            }

            string current = candidate;

            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current)) && IsExistingPathReparsePoint(current))
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

                current = NormalizeFullPath(parent);
            }
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsExistingPathReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsIoFailure(ex) || IsPathFailure(ex))
        {
            return true;
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = NormalizeFullPath(candidatePath);
        string root = NormalizeFullPath(rootPath);

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(EnsureDirectorySeparatorSuffix(root), StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right)
    {
        return string.Equals(
            NormalizeFullPath(left),
            NormalizeFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFullPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string? root = Path.GetPathRoot(fullPath);

        if (!string.IsNullOrWhiteSpace(root)
            && fullPath.Equals(root, StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        return fullPath.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
    }

    private static string EnsureDirectorySeparatorSuffix(string path)
    {
        return path.EndsWith(Path.DirectorySeparatorChar)
               || path.EndsWith(Path.AltDirectorySeparatorChar)
            ? path
            : path + Path.DirectorySeparatorChar;
    }

    private static bool IsPathFailure(Exception ex)
    {
        return ex is ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException;
    }

    private static bool IsIoFailure(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException;
    }

    private static BinSectorProbeResult CreateResult(
        string path,
        long length,
        int? sectorSize,
        BinTrackKind kind,
        byte? modeByte,
        int syncObservedAt,
        params BinSectorProbeReasonCode[] reasons)
    {
        return new BinSectorProbeResult(
            path,
            length,
            sectorSize,
            kind,
            modeByte,
            syncObservedAt,
            reasons.Length == 0 ? [] : [.. reasons]);
    }

    private static BinSectorProbeResult CreateResult(
        string path,
        long length,
        int? sectorSize,
        BinTrackKind kind,
        byte? modeByte,
        int syncObservedAt,
        List<BinSectorProbeReasonCode> reasons)
    {
        return new BinSectorProbeResult(
            path,
            length,
            sectorSize,
            kind,
            modeByte,
            syncObservedAt,
            reasons.Count == 0 ? [] : [.. reasons]);
    }
}