using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Core.Input;

public enum HardDiskImageEvidence
{
    Accepted = 0,
    NotHardDiskExtension = 1,
    Empty = 2,
    NotSectorAligned = 3,
    DescriptorSibling = 4,
    ReferencedByDescriptor = 5,
    CdSyncHeader = 6,
    Iso9660Volume = 7,
    ProbeUnavailable = 8
}

/// <summary>
/// Decides whether a raw .img/.hdd file may be treated as a hard-disk image.
/// Raw hard-disk images have no magic bytes, so this check fails closed on any
/// sign that the file belongs to a CD/DVD set or carries a disc file system.
/// </summary>
public static class HardDiskImageEvidenceProbe
{
    public const int SectorSizeBytes = 512;

    private const int MaxSiblingDescriptors = 64;
    private const long MaxDescriptorBytes = 256 * 1024;

    // ISO9660 primary volume descriptor ("CD001" at byte 1 of sector 16) for
    // 2048-byte cooked, MODE1/2352 and MODE2/2352 (form 1) layouts.
    private static readonly long[] Iso9660SignatureOffsets =
    [
        (16L * 2048L) + 1L,
        (16L * 2352L) + 16L + 1L,
        (16L * 2352L) + 24L + 1L
    ];

    private static readonly byte[] Iso9660Signature = "CD001"u8.ToArray();

    private static readonly byte[] CdSyncPattern =
    [
        0x00, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x00
    ];

    private static readonly string[] SameStemDescriptorExtensions =
    [
        ".cue", ".ccd", ".toc", ".gdi", ".mds"
    ];

    private static readonly HashSet<string> ReferencingDescriptorExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".cue", ".gdi", ".toc" };

    public static bool IsHardDiskImageExtension(string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return false;
        }

        string normalized = extension.Trim();
        if (normalized[0] != '.')
        {
            normalized = "." + normalized;
        }

        return normalized.Equals(".img", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(".hdd", StringComparison.OrdinalIgnoreCase);
    }

    public static HardDiskImageEvidence Evaluate(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)
            || !IsHardDiskImageExtension(Path.GetExtension(fullPath)))
        {
            return HardDiskImageEvidence.NotHardDiskExtension;
        }

        try
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                return HardDiskImageEvidence.ProbeUnavailable;
            }

            if (file.Length <= 0)
            {
                return HardDiskImageEvidence.Empty;
            }

            if (file.Length % SectorSizeBytes != 0)
            {
                return HardDiskImageEvidence.NotSectorAligned;
            }

            if (HasSameStemDescriptor(file))
            {
                return HardDiskImageEvidence.DescriptorSibling;
            }

            if (IsReferencedBySiblingDescriptor(file))
            {
                return HardDiskImageEvidence.ReferencedByDescriptor;
            }

            return ProbeContent(file);
        }
        catch (Exception ex) when (IsExpectedIoException(ex))
        {
            return HardDiskImageEvidence.ProbeUnavailable;
        }
    }

    private static bool HasSameStemDescriptor(FileInfo file)
    {
        string? directory = file.DirectoryName;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        string stem = Path.GetFileNameWithoutExtension(file.Name);
        foreach (string extension in SameStemDescriptorExtensions)
        {
            if (File.Exists(Path.Combine(directory, stem + extension)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReferencedBySiblingDescriptor(FileInfo file)
    {
        string? directory = file.DirectoryName;
        if (string.IsNullOrWhiteSpace(directory))
        {
            return false;
        }

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.Directory
        };

        int inspected = 0;
        foreach (string candidate in Directory.EnumerateFiles(directory, "*", options))
        {
            if (!ReferencingDescriptorExtensions.Contains(Path.GetExtension(candidate)))
            {
                continue;
            }

            if (++inspected > MaxSiblingDescriptors)
            {
                // Too many descriptors to prove the image is not a track: fail closed.
                return true;
            }

            var descriptor = new FileInfo(candidate);
            if (descriptor.Length <= 0 || descriptor.Length > MaxDescriptorBytes)
            {
                continue;
            }

            string text = File.ReadAllText(candidate, Encoding.UTF8);
            if (text.Contains(file.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static HardDiskImageEvidence ProbeContent(FileInfo file)
    {
        using FileStream stream = new(
            file.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.RandomAccess);

        Span<byte> sync = stackalloc byte[12];
        if (ReadAt(stream, 0, sync) == sync.Length && sync.SequenceEqual(CdSyncPattern))
        {
            return HardDiskImageEvidence.CdSyncHeader;
        }

        Span<byte> signature = stackalloc byte[5];
        foreach (long offset in Iso9660SignatureOffsets)
        {
            if (offset + signature.Length > stream.Length)
            {
                continue;
            }

            if (ReadAt(stream, offset, signature) == signature.Length
                && signature.SequenceEqual(Iso9660Signature))
            {
                return HardDiskImageEvidence.Iso9660Volume;
            }
        }

        return HardDiskImageEvidence.Accepted;
    }

    private static int ReadAt(FileStream stream, long offset, Span<byte> buffer)
    {
        stream.Position = offset;
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool IsExpectedIoException(Exception ex) =>
        ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or System.Security.SecurityException;
}
