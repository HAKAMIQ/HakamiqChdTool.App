using HakamiqChdTool.App.Core.Disc;
using System.Globalization;
using System.IO;
using System.Text;

namespace HakamiqChdTool.App.Services;

public enum DiskPreflightMode
{
    CreateChd,
    ExtractFromChd
}

public sealed record DiskPreflightResult(
    bool HasEnoughSpace,
    string TargetRoot,
    long InputBytes,
    long EstimatedRequiredBytes,
    long AvailableFreeBytes,
    string MessageKey,
    string OperationKey);

public sealed class DiskPreflightException : IOException
{
    public DiskPreflightException(DiskPreflightResult result)
        : base("Disk preflight failed.")
    {
        Result = result ?? throw new ArgumentNullException(nameof(result));
    }

    public DiskPreflightResult Result { get; }
}

public static class DiskSpacePreflightService
{
    private const double CdCompressionRatioEstimate = 0.75d;
    private const double DvdCompressionRatioEstimate = 0.82d;
    private const double ExtractFromChdExpansionRatioEstimate = 2.25d;
    private const double SafetyMultiplier = 1.18d;
    private const long MinimumSafetyBytes = 512L * 1024L * 1024L;
    private const long PerReferencedTrackSafetyBytes = 16L * 1024L * 1024L;

    public const string SuccessMessageKey = "LocDiskPreflight_SpaceEnough";
    public const string FailureMessageKey = "LocDiskPreflight_SpaceNotEnough";
    public const string InvalidInputPathMessageKey = "LocDiskPreflight_InvalidInputPath";
    public const string InvalidOutputPathMessageKey = "LocDiskPreflight_InvalidOutputPath";
    public const string InvalidCommandMessageKey = "LocDiskPreflight_InvalidCommand";
    public const string OutputDirectoryMissingMessageKey = "LocDiskPreflight_OutputDirectoryMissing";
    public const string TargetRootMissingMessageKey = "LocDiskPreflight_TargetRootMissing";
    public const string TargetDriveNotReadyMessageKey = "LocDiskPreflight_TargetDriveNotReady";
    public const string InputFileNotFoundMessageKey = "LocDiskPreflight_InputFileNotFound";

    public const string OperationCreateCdKey = "LocDiskPreflight_OperationCreateCd";
    public const string OperationCreateDvdKey = "LocDiskPreflight_OperationCreateDvd";
    public const string OperationExtractCdKey = "LocDiskPreflight_OperationExtractCd";
    public const string OperationExtractDvdKey = "LocDiskPreflight_OperationExtractDvd";
    public const string OperationExtractHdKey = "LocDiskPreflight_OperationExtractHd";
    public const string OperationExtractRawKey = "LocDiskPreflight_OperationExtractRaw";
    public const string OperationExtractUnknownKey = "LocDiskPreflight_OperationExtractUnknown";
    public const string OperationCreateUnknownKey = "LocDiskPreflight_OperationCreateUnknown";

    public static DiskPreflightResult CheckOrThrow(
        string inputPath,
        string outputPath,
        string chdmanCommand,
        long? logicalOutputBytes = null)
    {
        if (string.IsNullOrWhiteSpace(chdmanCommand))
        {
            throw new ArgumentException(InvalidCommandMessageKey, nameof(chdmanCommand));
        }

        DiskPreflightMode mode = chdmanCommand.StartsWith("extract", StringComparison.OrdinalIgnoreCase)
            ? DiskPreflightMode.ExtractFromChd
            : DiskPreflightMode.CreateChd;

        DiskPreflightResult result = Check(inputPath, outputPath, chdmanCommand, mode, logicalOutputBytes);
        if (!result.HasEnoughSpace)
        {
            throw new DiskPreflightException(result);
        }

        return result;
    }

    public static DiskPreflightResult Check(
        string inputPath,
        string outputPath,
        string chdmanCommand,
        DiskPreflightMode mode,
        long? logicalOutputBytes = null)
    {
        if (string.IsNullOrWhiteSpace(inputPath))
        {
            throw new ArgumentException(InvalidInputPathMessageKey, nameof(inputPath));
        }

        if (string.IsNullOrWhiteSpace(outputPath))
        {
            throw new ArgumentException(InvalidOutputPathMessageKey, nameof(outputPath));
        }

        if (string.IsNullOrWhiteSpace(chdmanCommand))
        {
            throw new ArgumentException(InvalidCommandMessageKey, nameof(chdmanCommand));
        }

        string fullInput = GetFullPathOrThrow(inputPath, InvalidInputPathMessageKey, nameof(inputPath));
        string fullOutput = GetFullPathOrThrow(outputPath, InvalidOutputPathMessageKey, nameof(outputPath));

        string? outputDirectory = Path.GetDirectoryName(fullOutput);
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidOperationException(OutputDirectoryMissingMessageKey);
        }

        string? root = Path.GetPathRoot(fullOutput);
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(TargetRootMissingMessageKey);
        }

        DriveInfo drive = GetReadyDriveOrThrow(root);
        long inputBytes = EstimateInputBytes(fullInput);
        long estimatedRequired = EstimateRequiredBytes(inputBytes, chdmanCommand, mode, logicalOutputBytes);
        long available = GetAvailableFreeSpaceOrThrow(drive);
        bool hasEnough = available >= estimatedRequired;

        return new DiskPreflightResult(
            hasEnough,
            drive.Name,
            inputBytes,
            estimatedRequired,
            available,
            hasEnough ? SuccessMessageKey : FailureMessageKey,
            DescribeOperationKey(chdmanCommand, mode));
    }

    private static string GetFullPathOrThrow(string path, string messageKey, string parameterName)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new ArgumentException(messageKey, parameterName, ex);
        }
    }

    private static DriveInfo GetReadyDriveOrThrow(string root)
    {
        try
        {
            DriveInfo drive = new(root);
            if (!drive.IsReady)
            {
                throw new IOException(TargetDriveNotReadyMessageKey);
            }

            return drive;
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex) when (ex is ArgumentException or UnauthorizedAccessException or NotSupportedException)
        {
            throw new IOException(TargetDriveNotReadyMessageKey, ex);
        }
    }

    private static long GetAvailableFreeSpaceOrThrow(DriveInfo drive)
    {
        try
        {
            return drive.AvailableFreeSpace;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(TargetDriveNotReadyMessageKey, ex);
        }
    }

    private static long EstimateInputBytes(string inputPath)
    {
        if (!File.Exists(inputPath))
        {
            throw new FileNotFoundException(InputFileNotFoundMessageKey, inputPath);
        }

        try
        {
            string extension = Path.GetExtension(inputPath).ToLowerInvariant();
            long primaryBytes = new FileInfo(inputPath).Length;
            long referencedBytes = extension switch
            {
                ".cue" => EstimateCueReferencedBytes(inputPath),
                ".gdi" => EstimateGdiReferencedBytes(inputPath),
                ".toc" => EstimateTocReferencedBytes(inputPath),
                _ => 0L
            };

            return Math.Max(SaturatingAdd(primaryBytes, referencedBytes), 1L);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new IOException(InvalidInputPathMessageKey, ex);
        }
    }

    private static long EstimateRequiredBytes(long inputBytes, string command, DiskPreflightMode mode, long? logicalOutputBytes)
    {
        if (mode == DiskPreflightMode.ExtractFromChd && logicalOutputBytes is long logicalBytes && logicalBytes > 0)
        {
            double logicalEstimate = Math.Ceiling(logicalBytes * SafetyMultiplier);
            long estimatedFromMetadata = logicalEstimate >= long.MaxValue ? long.MaxValue : (long)logicalEstimate;
            return Math.Max(estimatedFromMetadata, MinimumSafetyBytes);
        }

        double ratio = mode == DiskPreflightMode.ExtractFromChd
            ? ExtractFromChdExpansionRatioEstimate
            : string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
                ? CdCompressionRatioEstimate
                : DvdCompressionRatioEstimate;

        double estimate = Math.Ceiling(inputBytes * ratio * SafetyMultiplier);
        long estimated = estimate >= long.MaxValue ? long.MaxValue : (long)estimate;
        return Math.Max(estimated, MinimumSafetyBytes);
    }

    private static long EstimateCueReferencedBytes(string cuePath)
    {
        string directory = Path.GetDirectoryName(cuePath) ?? string.Empty;
        long total = 0L;
        int referencedTracks = 0;

        foreach (string line in ReadMetadataLines(cuePath))
        {
            string? name = TryExtractCueFileName(line);
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            if (!TryGetReferencedFileLength(directory, name, out long length))
            {
                continue;
            }

            referencedTracks++;
            total = SaturatingAdd(total, length);
        }

        return SaturatingAdd(total, referencedTracks * PerReferencedTrackSafetyBytes);
    }

    private static long EstimateGdiReferencedBytes(string gdiPath)
    {
        string directory = Path.GetDirectoryName(gdiPath) ?? string.Empty;
        long total = 0L;
        int referencedTracks = 0;
        bool skippedHeader = false;

        foreach (string raw in ReadMetadataLines(gdiPath))
        {
            if (!skippedHeader)
            {
                skippedHeader = true;
                continue;
            }

            if (!TryExtractGdiFileName(raw, out string name))
            {
                continue;
            }

            if (!TryGetReferencedFileLength(directory, name, out long length))
            {
                continue;
            }

            referencedTracks++;
            total = SaturatingAdd(total, length);
        }

        return SaturatingAdd(total, referencedTracks * PerReferencedTrackSafetyBytes);
    }

    private static long EstimateTocReferencedBytes(string tocPath)
    {
        string directory = Path.GetDirectoryName(tocPath) ?? string.Empty;
        long total = 0L;
        int referencedTracks = 0;

        foreach (string raw in ReadMetadataLines(tocPath))
        {
            if (!TryExtractTocFileName(raw, out string name))
            {
                continue;
            }

            if (!TryGetReferencedFileLength(directory, name, out long length))
            {
                continue;
            }

            referencedTracks++;
            total = SaturatingAdd(total, length);
        }

        return SaturatingAdd(total, referencedTracks * PerReferencedTrackSafetyBytes);
    }

    private static IEnumerable<string> ReadMetadataLines(string path)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 16 * 1024,
            FileOptions.SequentialScan);

        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 16 * 1024,
            leaveOpen: false);

        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static bool TryGetReferencedFileLength(string baseDirectory, string referencedName, out long length)
    {
        length = 0L;

        if (string.IsNullOrWhiteSpace(baseDirectory) || string.IsNullOrWhiteSpace(referencedName))
        {
            return false;
        }

        try
        {
            string root = Path.GetFullPath(baseDirectory);
            string candidate = Path.GetFullPath(Path.Combine(root, referencedName));

            if (!IsUnderDirectory(root, candidate) || !File.Exists(candidate))
            {
                return false;
            }

            length = new FileInfo(candidate).Length;
            return length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static string? TryExtractCueFileName(string rawLine) =>
        CueSheetFileStatementReader.TryRead(rawLine, out string fileName, out _)
            ? fileName
            : null;

    private static bool TryExtractGdiFileName(string line, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = line.AsSpan().Trim();
        if (span.Length == 0 || span[0] == '#')
        {
            return false;
        }

        for (int field = 0; field < 4; field++)
        {
            if (!TryConsumeToken(ref span, out _))
            {
                return false;
            }
        }

        span = span.TrimStart();
        if (span.Length == 0)
        {
            return false;
        }

        if (span[0] == '"')
        {
            int closingQuote = span[1..].IndexOf('"');
            if (closingQuote < 0)
            {
                return false;
            }

            fileName = span.Slice(1, closingQuote).ToString().Trim();
            return fileName.Length > 0;
        }

        if (!TryConsumeToken(ref span, out ReadOnlySpan<char> token))
        {
            return false;
        }

        fileName = token.Trim('"').ToString().Trim();
        return fileName.Length > 0;
    }

    private static bool TryExtractTocFileName(string line, out string fileName)
    {
        fileName = string.Empty;

        ReadOnlySpan<char> span = line.AsSpan().TrimStart();
        if (span.Length == 0 || span.StartsWith("//".AsSpan(), StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryConsumeToken(ref span, out ReadOnlySpan<char> command))
        {
            return false;
        }

        if (!command.Equals("FILE".AsSpan(), StringComparison.OrdinalIgnoreCase)
            && !command.Equals("AUDIOFILE".AsSpan(), StringComparison.OrdinalIgnoreCase)
            && !command.Equals("DATAFILE".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        span = span.TrimStart();
        if (span.Length == 0)
        {
            return false;
        }

        if (span[0] == '"')
        {
            int closingQuote = span[1..].IndexOf('"');
            if (closingQuote < 0)
            {
                return false;
            }

            fileName = span.Slice(1, closingQuote).ToString().Trim();
            return fileName.Length > 0;
        }

        if (!TryConsumeToken(ref span, out ReadOnlySpan<char> token))
        {
            return false;
        }

        fileName = token.Trim('"').ToString().Trim();
        return fileName.Length > 0;
    }

    private static bool TryConsumeToken(ref ReadOnlySpan<char> span, out ReadOnlySpan<char> token)
    {
        span = span.TrimStart();
        token = default;

        if (span.Length == 0)
        {
            return false;
        }

        int separator = IndexOfWhiteSpace(span);
        if (separator < 0)
        {
            token = span;
            span = [];
            return token.Length > 0;
        }

        token = span[..separator];
        span = span[(separator + 1)..];
        return token.Length > 0;
    }

    public static string DescribeOperationKey(string command, DiskPreflightMode mode)
    {
        string normalizedCommand = command.Trim().ToLowerInvariant();

        if (mode == DiskPreflightMode.ExtractFromChd)
        {
            return normalizedCommand switch
            {
                "extractcd" => OperationExtractCdKey,
                "extractdvd" => OperationExtractDvdKey,
                "extracthd" => OperationExtractHdKey,
                "extractraw" => OperationExtractRawKey,
                _ => OperationExtractUnknownKey
            };
        }

        return normalizedCommand switch
        {
            "createcd" => OperationCreateCdKey,
            "createdvd" => OperationCreateDvdKey,
            _ => OperationCreateUnknownKey
        };
    }

    public static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return string.Create(CultureInfo.InvariantCulture, $"{value:0.##} {units[unit]}");
    }

    internal static long EstimateInputBytesForWorkflow(string inputPath)
    {
        string fullInput = GetFullPathOrThrow(inputPath, InvalidInputPathMessageKey, nameof(inputPath));

        if (File.Exists(fullInput))
        {
            return EstimateInputBytes(fullInput);
        }

        if (Directory.Exists(fullInput))
        {
            return EstimateDirectoryBytes(fullInput);
        }

        throw new FileNotFoundException(InputFileNotFoundMessageKey, fullInput);
    }

    private static long EstimateDirectoryBytes(string directoryPath)
    {
        try
        {
            ThrowIfReparsePoint(directoryPath);

            long total = 0L;
            var pendingDirectories = new Stack<string>();
            pendingDirectories.Push(directoryPath);

            while (pendingDirectories.Count > 0)
            {
                string currentDirectory = pendingDirectories.Pop();
                ThrowIfReparsePoint(currentDirectory);

                foreach (string filePath in Directory.EnumerateFiles(currentDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    ThrowIfReparsePoint(filePath);

                    long length = new FileInfo(filePath).Length;
                    if (length > 0)
                    {
                        total = SaturatingAdd(total, length);
                    }
                }

                foreach (string childDirectory in Directory.EnumerateDirectories(currentDirectory, "*", SearchOption.TopDirectoryOnly))
                {
                    ThrowIfReparsePoint(childDirectory);
                    pendingDirectories.Push(childDirectory);
                }
            }

            return Math.Max(total, 1L);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new IOException(InvalidInputPathMessageKey, ex);
        }
    }

    private static void ThrowIfReparsePoint(string path)
    {
        if (IsReparsePoint(path))
        {
            throw new IOException(InvalidInputPathMessageKey);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new IOException(InvalidInputPathMessageKey, ex);
        }
    }

    internal static string GetDriveRootForPath(string path)
    {
        string fullPath = GetFullPathOrThrow(path, TargetRootMissingMessageKey, nameof(path));
        string? root = Path.GetPathRoot(fullPath);

        if (string.IsNullOrWhiteSpace(root))
        {
            throw new InvalidOperationException(TargetRootMissingMessageKey);
        }

        return GetReadyDriveOrThrow(root).Name;
    }

    internal static long GetAvailableFreeBytes(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            throw new InvalidOperationException(TargetRootMissingMessageKey);
        }

        DriveInfo drive = GetReadyDriveOrThrow(driveRoot);
        return GetAvailableFreeSpaceOrThrow(drive);
    }

    internal static long GetTotalSizeBytes(string driveRoot)
    {
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            throw new InvalidOperationException(TargetRootMissingMessageKey);
        }

        DriveInfo drive = GetReadyDriveOrThrow(driveRoot);

        try
        {
            return Math.Max(0L, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(TargetDriveNotReadyMessageKey, ex);
        }
    }

    internal static long EstimateWorkflowRequiredBytes(long sourceBytes, double multiplier, long minimumRequiredBytes)
    {
        long normalizedSourceBytes = Math.Max(1L, sourceBytes);
        double estimate = Math.Ceiling(normalizedSourceBytes * Math.Max(0.01d, multiplier));
        long estimated = estimate >= long.MaxValue ? long.MaxValue : (long)estimate;

        return Math.Max(estimated, Math.Max(1L, minimumRequiredBytes));
    }

    internal static DiskSpaceRequirement CreateRequirementForPath(
        string path,
        long requiredBytes,
        string purpose)
    {
        string driveRoot = GetDriveRootForPath(path);
        return new DiskSpaceRequirement(driveRoot, requiredBytes, purpose);
    }

    private static int IndexOfWhiteSpace(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i]))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsUnderDirectory(string baseDirectory, string candidate)
    {
        string root = Path.GetFullPath(baseDirectory);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
        {
            root += Path.DirectorySeparatorChar;
        }

        string path = Path.GetFullPath(candidate);
        return path.StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        if (right < 0 && left < long.MinValue - right)
        {
            return long.MinValue;
        }

        return left + right;
    }
}