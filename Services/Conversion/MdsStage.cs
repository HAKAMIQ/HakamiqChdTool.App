using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

/// <summary>
/// Places an Alcohol 120% .mdf inside the process temp workspace for chdman.
/// Plain 2352- or 2048-byte images are hard-linked or copied by
/// <see cref="CloneCdImageStager"/>. Images that carry 96 bytes of subchannel
/// data after every sector are rewritten to plain 2352-byte sectors, after a
/// free-space check. Nothing is ever written to the source folder.
/// </summary>
internal static class AlcoholMdfStager
{
    private const long FreeSpaceReserveBytes = 64L * 1024L * 1024L;
    private const int SectorsPerChunk = 256;

    public static Task<bool> TryStageAsync(
        AlcoholMdsLayout layout,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(layout);

        return layout.SectorFormat == AlcoholMdsSectorFormat.Raw2352WithSubchannel
            ? TryStripSubchannelAsync(layout.ImagePath, destinationPath, layout.SectorCount, cancellationToken)
            : CloneCdImageStager.TryStageAsync(layout.ImagePath, destinationPath, cancellationToken);
    }

    internal static async Task<bool> TryStripSubchannelAsync(
        string sourceImagePath,
        string destinationPath,
        long sectorCount,
        CancellationToken cancellationToken)
    {
        const int InputSector = AlcoholMdsCueBuilder.RawWithSubchannelSectorBytes;
        const int OutputSector = AlcoholMdsCueBuilder.RawSectorBytes;

        var source = new FileInfo(sourceImagePath);
        if (!source.Exists
            || sectorCount <= 0
            || source.Length != sectorCount * InputSector
            || File.Exists(destinationPath))
        {
            return false;
        }

        long outputBytes = sectorCount * OutputSector;
        string? root = Path.GetPathRoot(Path.GetFullPath(destinationPath));
        if (string.IsNullOrWhiteSpace(root)
            || new DriveInfo(root).AvailableFreeSpace < outputBytes + FreeSpaceReserveBytes)
        {
            return false;
        }

        byte[] input = new byte[InputSector * SectorsPerChunk];
        byte[] output = new byte[OutputSector * SectorsPerChunk];
        try
        {
            await using (var reader = new FileStream(source.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, input.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            await using (var writer = new FileStream(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, output.Length, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                long remaining = sectorCount;
                while (remaining > 0)
                {
                    int sectors = (int)Math.Min(remaining, SectorsPerChunk);
                    await reader.ReadExactlyAsync(input.AsMemory(0, sectors * InputSector), cancellationToken).ConfigureAwait(false);
                    for (int i = 0; i < sectors; i++)
                    {
                        Buffer.BlockCopy(input, i * InputSector, output, i * OutputSector, OutputSector);
                    }

                    await writer.WriteAsync(output.AsMemory(0, sectors * OutputSector), cancellationToken).ConfigureAwait(false);
                    remaining -= sectors;
                }
            }

            if (new FileInfo(destinationPath).Length == outputBytes)
            {
                return true;
            }
        }
        catch (OperationCanceledException)
        {
            TryDelete(destinationPath);
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(destinationPath);
            return false;
        }

        TryDelete(destinationPath);
        return false;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The temp workspace is deleted as a whole afterwards.
        }
    }
}
