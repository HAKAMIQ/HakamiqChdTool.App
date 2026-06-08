using HakamiqChdTool.App.Models.PlayStation.BluRayAnalysis;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services.PlayStation.BluRayAnalysis;

public sealed class BluRayIsoAnalysisService
{
    private const int SectorSize = 2048;
    private const long PlayStation3MagicOffset = 0x800;
    private const long PlayStation3TitleIdOffset = 0x810;
    private const int TitleIdLength = 32;

    private static readonly byte[] PlayStation3Magic = Encoding.ASCII.GetBytes("PlayStation3");
    private static readonly byte[] Ps3DiscSfbPattern = Encoding.ASCII.GetBytes(".SFB");
    private static readonly byte[] ParamSfoPattern = [0x00, 0x50, 0x53, 0x46];
    private static readonly byte[] EbootBinPattern = Encoding.ASCII.GetBytes("EBOOT.BIN");
    private static readonly byte[] EbootBinUtf16Pattern = Encoding.Unicode.GetBytes("EBOOT.BIN");

    public BluRayIsoAnalysisResult Analyze(
        string path,
        BluRayAnalysisProfile profile = BluRayAnalysisProfile.Balanced,
        IProgress<BluRayAnalysisProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        FileInfo file = new(path);
        if (!file.Exists)
        {
            throw new FileNotFoundException(null, path);
        }

        AnalysisProfileSettings settings = AnalysisProfileSettings.For(profile, file.Length);
        Report(progress, BluRayAnalysisStage.Preparing, 6, file.Name);

        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            options: FileOptions.RandomAccess);

        Report(progress, BluRayAnalysisStage.ReadingDiscHeader, 10, file.Name);
        bool isSectorSizeAligned = file.Length > 0 && file.Length % SectorSize == 0;
        bool hasUdfAnchor = HasUdfAnchor(stream, file.Length, cancellationToken);
        Report(progress, BluRayAnalysisStage.ReadingDiscHeader, 18, file.Name);
        bool hasMagic = HasPatternAt(stream, PlayStation3MagicOffset, PlayStation3Magic, cancellationToken);
        string discTitleId = ReadDiscTitleId(stream, cancellationToken);

        Report(progress, BluRayAnalysisStage.CheckingDiscStructure, 26, file.Name);
        long sfbOffset = FindPs3DiscSfb(stream, settings.StructureScanBytes, progress, 28, 43, cancellationToken);
        long sfoOffset = FindParamSfo(stream, settings.StructureScanBytes, progress, 44, 59, cancellationToken);
        long ebootOffset = FindAnyPatternPosition(stream, settings.StructureScanBytes, progress, 60, 74, cancellationToken, EbootBinPattern, EbootBinUtf16Pattern);

        bool hasSfb = sfbOffset >= 0;
        bool hasSfo = sfoOffset >= 0;
        bool hasEboot = ebootOffset >= 0;

        Report(progress, BluRayAnalysisStage.SearchingMetadata, 76, file.Name);

        string sfbTitleId = hasSfb
            ? ReadSfbTitleId(stream, sfbOffset, cancellationToken)
            : string.Empty;

        ParamSfoData sfoData = hasSfo
            ? ReadParamSfo(stream, sfoOffset, cancellationToken)
            : ParamSfoData.Empty;

        BluRayCompressionEstimate estimate = EstimateCompression(stream, file.Length, settings, progress, cancellationToken);

        BluRayPs3DiscMetadata metadata = new(
            discTitleId,
            sfbTitleId,
            sfoData.TitleId,
            sfoData.Title,
            sfoData.AppVersion,
            sfoData.SystemVersion,
            isSectorSizeAligned,
            hasUdfAnchor,
            hasMagic,
            hasSfb,
            hasSfo,
            hasEboot);

        IReadOnlyList<BluRayCheckResult> checks =
        [
            new BluRayCheckResult(BluRayCheckCode.SectorSizeAligned, isSectorSizeAligned, isSectorSizeAligned ? SectorSize.ToString() : file.Length.ToString()),
            new BluRayCheckResult(BluRayCheckCode.UdfAnchor, hasUdfAnchor),
            new BluRayCheckResult(BluRayCheckCode.PlayStation3Magic, hasMagic),
            new BluRayCheckResult(BluRayCheckCode.PreferredTitleId, !string.IsNullOrWhiteSpace(metadata.PreferredTitleId), metadata.PreferredTitleId),
            new BluRayCheckResult(BluRayCheckCode.Ps3DiscSfb, hasSfb, hasSfb ? FormatOffset(sfbOffset) : string.Empty),
            new BluRayCheckResult(BluRayCheckCode.Ps3DiscSfbTitleId, !string.IsNullOrWhiteSpace(sfbTitleId), sfbTitleId),
            new BluRayCheckResult(BluRayCheckCode.ParamSfo, hasSfo, hasSfo ? FormatOffset(sfoOffset) : string.Empty),
            new BluRayCheckResult(BluRayCheckCode.ParamSfoTitleId, !string.IsNullOrWhiteSpace(sfoData.TitleId), sfoData.TitleId),
            new BluRayCheckResult(BluRayCheckCode.ParamSfoTitle, !string.IsNullOrWhiteSpace(sfoData.Title), sfoData.Title),
            new BluRayCheckResult(BluRayCheckCode.EbootBin, hasEboot, hasEboot ? FormatOffset(ebootOffset) : string.Empty)
        ];

        Report(progress, BluRayAnalysisStage.BuildingReport, 97, file.Name);
        return new BluRayIsoAnalysisResult(path, file.Length, metadata, estimate, checks);
    }

    public bool TryAnalyze(
        string path,
        out BluRayIsoAnalysisResult? result,
        BluRayAnalysisProfile profile = BluRayAnalysisProfile.Quick,
        CancellationToken cancellationToken = default)
    {
        result = null;

        try
        {
            result = Analyze(path, profile, null, cancellationToken);
            return true;
        }
        catch (Exception ex) when (IsExpectedAnalysisException(ex))
        {
            return false;
        }
    }

    private static bool HasUdfAnchor(FileStream stream, long length, CancellationToken cancellationToken)
    {
        if (length < SectorSize * 257L)
        {
            return false;
        }

        long sectorCount = length / SectorSize;
        long[] candidates = [256, 512, Math.Max(0, sectorCount - 257)];

        foreach (long sector in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (sector <= 0 || sector >= sectorCount)
            {
                continue;
            }

            byte[] buffer = ReadBytesAt(stream, sector * SectorSize, 16, cancellationToken);
            if (IsAnchorDescriptorTag(buffer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAnchorDescriptorTag(byte[] buffer)
    {
        ReadOnlySpan<byte> tag = buffer.AsSpan();
        if (tag.Length < 16)
        {
            return false;
        }

        ushort identifier = BinaryPrimitives.ReadUInt16LittleEndian(tag);
        if (identifier != 2)
        {
            return false;
        }

        byte checksum = 0;
        for (int i = 0; i < 16; i++)
        {
            if (i != 4)
            {
                checksum += tag[i];
            }
        }

        return checksum == tag[4];
    }

    private static bool HasPatternAt(FileStream stream, long offset, byte[] pattern, CancellationToken cancellationToken)
    {
        if (stream.Length < offset + pattern.Length)
        {
            return false;
        }

        byte[] buffer = ReadBytesAt(stream, offset, pattern.Length, cancellationToken);
        return ByteArraysEqual(buffer, pattern);
    }

    private static string ReadDiscTitleId(FileStream stream, CancellationToken cancellationToken)
    {
        if (stream.Length < PlayStation3TitleIdOffset + TitleIdLength)
        {
            return string.Empty;
        }

        byte[] buffer = ReadBytesAt(stream, PlayStation3TitleIdOffset, TitleIdLength, cancellationToken);
        if (buffer.Length != TitleIdLength)
        {
            return string.Empty;
        }

        string text = Encoding.ASCII.GetString(buffer).Replace("\0", string.Empty, StringComparison.Ordinal).Trim();
        return LooksLikeTitleId(text) ? text : string.Empty;
    }

    private static long FindPs3DiscSfb(FileStream stream, long maxBytes, IProgress<BluRayAnalysisProgress>? progress, double startPercent, double endPercent, CancellationToken cancellationToken)
    {
        long[] likelyOffsets = [0x310000, 0x300000, 0x320000];
        long found = FindPatternAtLikelyOffsets(stream, Ps3DiscSfbPattern, likelyOffsets, cancellationToken);
        return found >= 0
            ? found
            : FindPatternPosition(stream, Ps3DiscSfbPattern, maxBytes, progress, BluRayAnalysisStage.CheckingDiscStructure, startPercent, endPercent, cancellationToken);
    }

    private static long FindParamSfo(FileStream stream, long maxBytes, IProgress<BluRayAnalysisProgress>? progress, double startPercent, double endPercent, CancellationToken cancellationToken)
    {
        long[] likelyOffsets = [0x310800, 0x300800, 0x320800];
        long found = FindPatternAtLikelyOffsets(stream, ParamSfoPattern, likelyOffsets, cancellationToken);
        return found >= 0
            ? found
            : FindPatternPosition(stream, ParamSfoPattern, maxBytes, progress, BluRayAnalysisStage.CheckingDiscStructure, startPercent, endPercent, cancellationToken);
    }

    private static long FindPatternAtLikelyOffsets(FileStream stream, byte[] pattern, IReadOnlyList<long> offsets, CancellationToken cancellationToken)
    {
        foreach (long offset in offsets)
        {
            if (HasPatternAt(stream, offset, pattern, cancellationToken))
            {
                return offset;
            }
        }

        return -1;
    }

    private static string ReadSfbTitleId(FileStream stream, long offset, CancellationToken cancellationToken)
    {
        byte[] buffer = ReadBytesAt(stream, offset, 64 * 1024, cancellationToken);
        return ExtractTitleIdFromAscii(buffer);
    }

    private static ParamSfoData ReadParamSfo(FileStream stream, long offset, CancellationToken cancellationToken)
    {
        byte[] buffer = ReadBytesAt(stream, offset, 128 * 1024, cancellationToken);
        return ParamSfoData.Parse(buffer);
    }

    private static byte[] ReadBytesAt(FileStream stream, long offset, int count, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (offset < 0 || count <= 0 || offset >= stream.Length)
        {
            return [];
        }

        int safeCount = (int)Math.Min(count, stream.Length - offset);
        byte[] buffer = new byte[safeCount];
        int total = ReadAt(stream, buffer.AsSpan(), offset, cancellationToken);

        if (total == safeCount)
        {
            return buffer;
        }

        byte[] resized = new byte[total];
        Array.Copy(buffer, resized, total);
        return resized;
    }

    private static int ReadAt(FileStream stream, Span<byte> buffer, long offset, CancellationToken cancellationToken)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int read = RandomAccess.Read(
                stream.SafeFileHandle,
                buffer[total..],
                offset + total);

            if (read <= 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static long FindAnyPatternPosition(FileStream stream, long maxBytes, IProgress<BluRayAnalysisProgress>? progress, double startPercent, double endPercent, CancellationToken cancellationToken, params byte[][] patterns)
    {
        long best = -1;

        for (int index = 0; index < patterns.Length; index++)
        {
            byte[] pattern = patterns[index];
            double patternStart = Lerp(startPercent, endPercent, patterns.Length <= 1 ? 0 : index / (double)patterns.Length);
            double patternEnd = Lerp(startPercent, endPercent, patterns.Length <= 1 ? 1 : (index + 1) / (double)patterns.Length);
            long position = FindPatternPosition(stream, pattern, maxBytes, progress, BluRayAnalysisStage.CheckingDiscStructure, patternStart, patternEnd, cancellationToken);
            if (position >= 0 && (best < 0 || position < best))
            {
                best = position;
            }
        }

        return best;
    }

    private static long FindPatternPosition(FileStream stream, byte[] pattern, long maxBytes, IProgress<BluRayAnalysisProgress>? progress, BluRayAnalysisStage stage, double startPercent, double endPercent, CancellationToken cancellationToken)
    {
        if (pattern.Length == 0 || maxBytes <= 0)
        {
            return -1;
        }

        const int bufferSize = 1024 * 1024;
        byte[] buffer = new byte[bufferSize + pattern.Length];
        int carry = 0;
        long scanLimit = Math.Min(maxBytes, stream.Length);
        long remaining = scanLimit;
        long absolute = 0;

        while (remaining > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportRange(progress, stage, startPercent, endPercent, absolute, scanLimit);

            int target = (int)Math.Min(bufferSize, remaining);
            int read = RandomAccess.Read(
                stream.SafeFileHandle,
                buffer.AsSpan(carry, target),
                absolute);

            if (read <= 0)
            {
                return -1;
            }

            int total = carry + read;
            int index = IndexOfPattern(buffer, total, pattern);
            if (index >= 0)
            {
                return absolute - carry + index;
            }

            carry = Math.Min(pattern.Length - 1, total);
            MoveCarry(buffer, total, carry);
            remaining -= read;
            absolute += read;
        }

        return -1;
    }

    private static bool ByteArraysEqual(byte[] left, byte[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        int difference = 0;
        for (int index = 0; index < left.Length; index++)
        {
            difference |= left[index] ^ right[index];
        }

        return difference == 0;
    }

    private static int IndexOfPattern(byte[] buffer, int length, byte[] pattern)
    {
        return buffer.AsSpan(0, length).IndexOf(pattern);
    }

    private static void MoveCarry(byte[] buffer, int total, int carry)
    {
        if (carry > 0)
        {
            buffer.AsSpan(total - carry, carry).CopyTo(buffer.AsSpan(0, carry));
        }
    }

    private static string ExtractTitleIdFromAscii(byte[] buffer)
    {
        ReadOnlySpan<byte> source = buffer.AsSpan();
        for (int i = 0; i < source.Length - 8; i++)
        {
            if (!IsAsciiUpperLetter(source[i]) || !IsAsciiUpperLetter(source[i + 1]) || !IsAsciiUpperLetter(source[i + 2]) || !IsAsciiUpperLetter(source[i + 3]))
            {
                continue;
            }

            if (source[i + 4] == (byte)'-')
            {
                if (i + 9 < source.Length && IsAsciiDigitRange(source.Slice(i + 5, 5)))
                {
                    string value = Encoding.ASCII.GetString(source.Slice(i, 10));
                    return LooksLikeTitleId(value) ? value : string.Empty;
                }
            }
            else if (IsAsciiDigitRange(source.Slice(i + 4, 5)))
            {
                string value = Encoding.ASCII.GetString(source.Slice(i, 9));
                return LooksLikeTitleId(value) ? value : string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeTitleId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        string normalized = value.Trim().Replace("-", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal);
        if (normalized.Length != 9)
        {
            return false;
        }

        return IsKnownPs3Prefix(normalized[..4]) && IsAsciiDigitRange(normalized.AsSpan(4, 5));
    }

    private static bool IsKnownPs3Prefix(string prefix)
    {
        return prefix switch
        {
            "BCUS" or "BLUS" or "BCES" or "BLES" or "BCJS" or "BLJM" or "BCAS" or "BLAS" or "NPEA" or "NPUB" or "NPJB" or "NPJA" or "NPHB" => true,
            _ => false
        };
    }

    private static bool IsAsciiUpperLetter(byte value)
    {
        return value >= 'A' && value <= 'Z';
    }

    private static bool IsAsciiDigitRange(ReadOnlySpan<char> values)
    {
        foreach (char value in values)
        {
            if (value < '0' || value > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiDigitRange(ReadOnlySpan<byte> values)
    {
        foreach (byte value in values)
        {
            if (value < '0' || value > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static void Report(IProgress<BluRayAnalysisProgress>? progress, BluRayAnalysisStage stage, double percent, string detail)
    {
        progress?.Report(new BluRayAnalysisProgress(stage, percent, detail));
    }

    private static void ReportRange(IProgress<BluRayAnalysisProgress>? progress, BluRayAnalysisStage stage, double startPercent, double endPercent, long completed, long total)
    {
        if (total <= 0)
        {
            Report(progress, stage, startPercent, string.Empty);
            return;
        }

        double ratio = Math.Clamp(completed / (double)total, 0, 1);
        Report(progress, stage, Lerp(startPercent, endPercent, ratio), string.Empty);
    }

    private static double Lerp(double start, double end, double ratio)
    {
        return start + (end - start) * Math.Clamp(ratio, 0, 1);
    }

    private static string FormatOffset(long offset)
    {
        return $"0x{offset:X}";
    }

    private static BluRayCompressionEstimate EstimateCompression(FileStream stream, long length, AnalysisProfileSettings settings, IProgress<BluRayAnalysisProgress>? progress, CancellationToken cancellationToken)
    {
        if (length <= 0)
        {
            return BluRayCompressionEstimate.Empty;
        }

        Report(progress, BluRayAnalysisStage.EstimatingCompression, 80, settings.ProfileName);

        byte[] buffer = new byte[settings.SampleBytes];
        long sampledBytes = 0;
        double weightedCompressedBytes = 0;
        int samples = 0;
        int zeroOrPatternSamples = 0;
        int incompressibleSamples = 0;

        int sampleIndex = 0;
        foreach (long offset in CreateSampleOffsets(length, settings.SampleCount, settings.SampleBytes))
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReportRange(progress, BluRayAnalysisStage.EstimatingCompression, 80, 96, sampleIndex, settings.SampleCount);
            sampleIndex++;

            int read = RandomAccess.Read(
                stream.SafeFileHandle,
                buffer.AsSpan(0, buffer.Length),
                offset);

            if (read <= 0)
            {
                continue;
            }

            SampleCompressionMetrics metrics = EstimateSample(buffer, read);
            sampledBytes += read;
            weightedCompressedBytes += metrics.EstimatedCompressedBytes;
            samples++;

            if (metrics.IsZeroOrPattern)
            {
                zeroOrPatternSamples++;
            }

            if (metrics.SavedPercent < 5)
            {
                incompressibleSamples++;
            }
        }

        if (sampledBytes <= 0 || samples <= 0)
        {
            return BluRayCompressionEstimate.Empty;
        }

        double ratio = Math.Clamp(weightedCompressedBytes / sampledBytes, 0.0, 1.0);
        long estimatedCompressed = Math.Max(0, (long)Math.Round(length * ratio));
        long estimatedSaved = Math.Max(0, length - estimatedCompressed);
        double savedPercent = estimatedSaved * 100.0 / length;

        return new BluRayCompressionEstimate(
            sampledBytes,
            estimatedCompressed,
            estimatedSaved,
            savedPercent,
            GetRating(savedPercent),
            settings.ProfileName,
            samples,
            zeroOrPatternSamples * 100.0 / samples,
            incompressibleSamples * 100.0 / samples,
            Math.Max(0, 100.0 - (incompressibleSamples * 100.0 / samples)));
    }

    private static IEnumerable<long> CreateSampleOffsets(long length, int sampleCount, int sampleBytes)
    {
        if (length <= sampleBytes || sampleCount <= 1)
        {
            yield return 0;
            yield break;
        }

        long maxOffset = Math.Max(0, length - sampleBytes);
        for (int i = 0; i < sampleCount; i++)
        {
            double position = sampleCount == 1 ? 0 : i / (double)(sampleCount - 1);
            long offset = (long)Math.Round(maxOffset * position);
            yield return AlignDown(offset, SectorSize);
        }
    }

    private static long AlignDown(long value, int alignment)
    {
        return value - value % alignment;
    }

    private static SampleCompressionMetrics EstimateSample(byte[] buffer, int length)
    {
        ReadOnlySpan<byte> sample = buffer.AsSpan(0, length);
        if (sample.IsEmpty)
        {
            return new SampleCompressionMetrics(0, 0, true);
        }

        if (IsZeroFilled(sample))
        {
            return new SampleCompressionMetrics(0, 100, true);
        }

        if (IsSingleBytePattern(sample))
        {
            return new SampleCompressionMetrics(1, 100, true);
        }

        Span<int> histogram = stackalloc int[256];
        foreach (byte value in sample)
        {
            histogram[value]++;
        }

        double entropy = CalculateEntropy(histogram, sample.Length);
        double entropyRatio = Math.Clamp(entropy / 8.0, 0.0, 1.0);
        double overheadRatio = sample.Length < 128 * 1024 ? 0.04 : 0.025;
        double estimatedRatio = Math.Clamp(entropyRatio + overheadRatio, 0.015, 1.0);
        long compressedBytes = Math.Min(sample.Length, (long)Math.Round(sample.Length * estimatedRatio));
        double savedPercent = (sample.Length - compressedBytes) * 100.0 / sample.Length;

        return new SampleCompressionMetrics(compressedBytes, savedPercent, false);
    }

    private static double CalculateEntropy(ReadOnlySpan<int> histogram, int total)
    {
        if (total <= 0)
        {
            return 0;
        }

        double entropy = 0;
        foreach (int count in histogram)
        {
            if (count <= 0)
            {
                continue;
            }

            double probability = count / (double)total;
            entropy -= probability * Math.Log2(probability);
        }

        return entropy;
    }

    private static bool IsZeroFilled(ReadOnlySpan<byte> sample)
    {
        foreach (byte value in sample)
        {
            if (value != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsSingleBytePattern(ReadOnlySpan<byte> sample)
    {
        byte first = sample[0];
        foreach (byte value in sample)
        {
            if (value != first)
            {
                return false;
            }
        }

        return true;
    }

    private static BluRayCompressionRating GetRating(double savedPercent)
    {
        if (savedPercent >= 50)
        {
            return BluRayCompressionRating.Excellent;
        }

        if (savedPercent >= 25)
        {
            return BluRayCompressionRating.Good;
        }

        if (savedPercent >= 15)
        {
            return BluRayCompressionRating.Moderate;
        }

        if (savedPercent >= 8)
        {
            return BluRayCompressionRating.Low;
        }

        return BluRayCompressionRating.Poor;
    }

    private static bool IsExpectedAnalysisException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or IOException
        or InvalidDataException
        or OperationCanceledException
        or OverflowException;

    private sealed record AnalysisProfileSettings(
        BluRayAnalysisProfile Profile,
        string ProfileName,
        long StructureScanBytes,
        int SampleCount,
        int SampleBytes)
    {
        public static AnalysisProfileSettings For(BluRayAnalysisProfile profile, long length)
        {
            return profile switch
            {
                BluRayAnalysisProfile.Deep => new AnalysisProfileSettings(profile, nameof(BluRayAnalysisProfile.Deep), Math.Min(length, 512L * 1024 * 1024), 160, 256 * 1024),
                BluRayAnalysisProfile.Balanced => new AnalysisProfileSettings(profile, nameof(BluRayAnalysisProfile.Balanced), Math.Min(length, 192L * 1024 * 1024), 96, 128 * 1024),
                _ => new AnalysisProfileSettings(BluRayAnalysisProfile.Quick, nameof(BluRayAnalysisProfile.Quick), Math.Min(length, 64L * 1024 * 1024), 48, 64 * 1024)
            };
        }
    }

    private readonly record struct SampleCompressionMetrics(long EstimatedCompressedBytes, double SavedPercent, bool IsZeroOrPattern);

    private sealed record ParamSfoData(string TitleId, string Title, string AppVersion, string SystemVersion)
    {
        public static ParamSfoData Empty { get; } = new(string.Empty, string.Empty, string.Empty, string.Empty);

        public static ParamSfoData Parse(byte[] buffer)
        {
            ReadOnlySpan<byte> data = buffer.AsSpan();
            if (data.Length < 20 || !data.Slice(0, 4).SequenceEqual(ParamSfoPattern))
            {
                return Empty;
            }

            uint keyTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(8, 4));
            uint dataTableOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(12, 4));
            uint entryCount = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(16, 4));

            if (keyTableOffset >= data.Length || dataTableOffset >= data.Length || entryCount > 4096)
            {
                return Empty;
            }

            Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);
            const int table = 20;
            for (uint i = 0; i < entryCount; i++)
            {
                int entryOffset = table + (int)i * 16;
                if (entryOffset + 16 > data.Length)
                {
                    break;
                }

                ushort keyOffset = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(entryOffset, 2));
                uint dataLength = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 4, 4));
                uint dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(entryOffset + 12, 4));
                long keyStart = keyTableOffset + keyOffset;
                long valueStart = dataTableOffset + dataOffset;

                if (keyStart < 0 || keyStart >= data.Length || valueStart < 0 || valueStart >= data.Length)
                {
                    continue;
                }

                string key = ReadNullTerminatedString(data.Slice((int)keyStart));
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                int safeLength = (int)Math.Min(dataLength, data.Length - valueStart);
                string value = safeLength > 0
                    ? ReadNullTerminatedString(data.Slice((int)valueStart, safeLength))
                    : string.Empty;

                values[key] = value;
            }

            return new ParamSfoData(
                values.TryGetValue("TITLE_ID", out string? titleId) ? titleId : string.Empty,
                values.TryGetValue("TITLE", out string? title) ? title : string.Empty,
                values.TryGetValue("APP_VER", out string? appVersion) ? appVersion : string.Empty,
                values.TryGetValue("PS3_SYSTEM_VER", out string? systemVersion) ? systemVersion : string.Empty);
        }

        private static string ReadNullTerminatedString(ReadOnlySpan<byte> data)
        {
            int length = data.IndexOf((byte)0);
            ReadOnlySpan<byte> slice = length >= 0 ? data.Slice(0, length) : data;
            if (slice.IsEmpty)
            {
                return string.Empty;
            }

            return Encoding.UTF8.GetString(slice).Trim();
        }
    }
}
