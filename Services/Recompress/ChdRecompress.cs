using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services.Conversion;
using Serilog;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Recompress;

public enum RecompressOutcome
{
    Saved = 0,
    Replaced = 1,
    NoGain = 2,
    SkippedExists = 3,
    Failed = 4,
    Cancelled = 5
}

/// <param name="CompressionPreset"><c>preset:default|fast|balanced|max</c>, like the conversion setting.</param>
/// <param name="ReplaceOriginal">Replace the source after the new file is verified, has the same data and is smaller.</param>
/// <param name="OutputDirectory">Where new files go when <paramref name="ReplaceOriginal"/> is off; must not be the source folder.</param>
public sealed record RecompressOptions(string CompressionPreset, bool ReplaceOriginal, string? OutputDirectory);

public sealed record RecompressResult(
    RecompressOutcome Outcome,
    string SourcePath,
    string? OutputPath,
    long OriginalBytes,
    long NewBytes,
    string MessageKey);

/// <summary>
/// Recompresses an existing CHD with <c>chdman copy</c>. The new file is written beside its final path under
/// a temporary name, checked with <c>chdman verify</c>, and compared with the source by the SHA-1 values
/// chdman reports (overall and data). It is kept only if it saves at least 1%. The source is replaced only when the
/// caller asks for it; otherwise the new file goes to another folder and never overwrites an existing file.
/// The source stays open for reading (no writers, no rename or delete) until the result is decided.
/// </summary>
internal sealed class ChdRecompressService
{
    public const string FailedKey = "LocRecompress_Failed";
    public const string NotChdKey = "LocRecompress_NotChd";
    public const string UnknownMediaKey = "LocRecompress_UnknownMedia";
    public const string SameFolderKey = "LocRecompress_SameFolder";
    public const string ExistsKey = "LocRecompress_Exists";
    public const string VerifyFailedKey = "LocRecompress_VerifyFailed";
    public const string HashMismatchKey = "LocRecompress_HashMismatch";
    public const string NoGainKey = "LocRecompress_NoGain";
    public const string SavedKey = "LocRecompress_Saved";
    public const string ReplacedKey = "LocRecompress_Replaced";
    public const string CancelledKey = "LocRecompress_Cancelled";
    public const string SourceBusyKey = "LocConversion_SourceNotStable";

    private readonly string _chdmanPath;
    private readonly ChdInfoService _info;
    private readonly ChdVerificationService _verify;

    public ChdRecompressService(string chdmanPath, ChdInfoService info, ChdVerificationService verify)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chdmanPath);
        _chdmanPath = chdmanPath;
        _info = info ?? throw new ArgumentNullException(nameof(info));
        _verify = verify ?? throw new ArgumentNullException(nameof(verify));
    }

    /// <summary>CD and GD-ROM CHDs take CD codecs; DVD, hard-disk and raw CHDs take the general codecs.</summary>
    public static bool TryResolveCodecs(string mediaType, string compressionPreset, out string codecs)
    {
        codecs = string.Empty;
        bool isCd = string.Equals(mediaType, "CD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "GD-ROM", StringComparison.OrdinalIgnoreCase);
        bool isOther = string.Equals(mediaType, "DVD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "HD-ROM", StringComparison.OrdinalIgnoreCase)
            || string.Equals(mediaType, "Raw", StringComparison.OrdinalIgnoreCase);
        if (!isCd && !isOther)
        {
            return false;
        }

        ChdCompressionResolution resolution = ChdCompressionPresetResolver.ResolveWithTruth(compressionPreset, isCd);
        codecs = string.IsNullOrWhiteSpace(resolution.ResolvedCompression)
            ? (isCd ? ChdCompressionResolution.MameCreateCdDefaultCompression : "lzma,zlib,huff,flac")
            : resolution.ResolvedCompression;
        return true;
    }

    /// <summary>Plans the final and temporary paths. Returns a message key when the plan is refused.</summary>
    public static string? PlanOutput(string sourcePath, RecompressOptions options, out string finalPath, out string pendingPath)
    {
        ArgumentNullException.ThrowIfNull(options);
        string source = Path.GetFullPath(sourcePath);
        string sourceDirectory = Path.GetDirectoryName(source)!;
        string fileName = Path.GetFileName(source);
        finalPath = string.Empty;
        pendingPath = string.Empty;

        string targetDirectory;
        if (options.ReplaceOriginal)
        {
            targetDirectory = sourceDirectory;
            finalPath = source;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(options.OutputDirectory))
            {
                return SameFolderKey;
            }

            targetDirectory = Path.GetFullPath(options.OutputDirectory);
            if (string.Equals(targetDirectory.TrimEnd('\\'), sourceDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            {
                return SameFolderKey;
            }

            finalPath = Path.Combine(targetDirectory, fileName);
            if (File.Exists(finalPath) || Directory.Exists(finalPath))
            {
                return ExistsKey;
            }
        }

        pendingPath = Path.Combine(targetDirectory, $"{Path.GetFileNameWithoutExtension(fileName)}.recompress-{Guid.NewGuid().ToString("N")[..8]}.chd");
        return null;
    }

    public async Task<RecompressResult> RecompressAsync(
        string sourcePath,
        RecompressOptions options,
        IProgress<int>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);
        string source = Path.GetFullPath(sourcePath);
        long originalBytes = TryGetLength(source);

        RecompressResult Result(RecompressOutcome outcome, string key, string? output = null, long newBytes = 0) =>
            new(outcome, source, output, originalBytes, newBytes, key);

        if (!string.Equals(Path.GetExtension(source), ".chd", StringComparison.OrdinalIgnoreCase) || !File.Exists(source))
        {
            return Result(RecompressOutcome.Failed, NotChdKey);
        }

        string? planError = PlanOutput(source, options, out string finalPath, out string pendingPath);
        if (planError is not null)
        {
            return Result(planError == ExistsKey ? RecompressOutcome.SkippedExists : RecompressOutcome.Failed, planError);
        }

        if (!SourceFileLease.TryAcquire([source], out SourceFileLease? lease, out _) || lease is null)
        {
            return Result(RecompressOutcome.Failed, SourceBusyKey);
        }

        bool keepPending = false;
        try
        {
            ConversionPathValidator.ThrowIfUnsafeForChdman(source, nameof(sourcePath));
            ConversionPathValidator.ThrowIfUnsafeForChdman(pendingPath, nameof(options));

            ChdInfoResult sourceInfo = await _info
                .ReadInfoAsync(_chdmanPath, source, cancellationToken: cancellationToken, callerHoldsExclusiveFileLease: true)
                .ConfigureAwait(false);
            if (sourceInfo.WasCancelled)
            {
                return Result(RecompressOutcome.Cancelled, CancelledKey);
            }

            if (!sourceInfo.IsSuccess)
            {
                return Result(RecompressOutcome.Failed, NotChdKey);
            }

            if (!TryResolveCodecs(sourceInfo.MediaType, options.CompressionPreset, out string codecs))
            {
                return Result(RecompressOutcome.Failed, UnknownMediaKey);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(pendingPath)!);
            ChdmanCliRunner.Result copy = await ChdmanCliRunner.ExecuteAsync(
                    _chdmanPath,
                    ["copy", "-i", source, "-o", pendingPath, "-c", codecs],
                    parseProgressPercent: true,
                    progress,
                    onProcessStarted: null,
                    cancellationToken)
                .ConfigureAwait(false);
            if (copy.WasCancelled || cancellationToken.IsCancellationRequested)
            {
                return Result(RecompressOutcome.Cancelled, CancelledKey);
            }

            if (copy.ExitCode != 0 || !File.Exists(pendingPath))
            {
                Log.Warning("chdman copy failed for {Source}: exit {ExitCode}. {Error}", source, copy.ExitCode, copy.StandardError);
                return Result(RecompressOutcome.Failed, FailedKey);
            }

            ChdVerificationResult verify = await _verify
                .VerifyAsync(_chdmanPath, pendingPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (verify.WasCancelled)
            {
                return Result(RecompressOutcome.Cancelled, CancelledKey);
            }

            if (!verify.IsSuccess)
            {
                return Result(RecompressOutcome.Failed, VerifyFailedKey);
            }

            ChdInfoResult newInfo = await _info
                .ReadInfoAsync(_chdmanPath, pendingPath, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (newInfo.WasCancelled)
            {
                return Result(RecompressOutcome.Cancelled, CancelledKey);
            }

            // A compressed source stores its SHA-1 values and the new file must match them. An uncompressed source
            // stores none; then chdman computed the new file's values from the source data during the copy, and
            // the verify above proved the new file decodes to them.
            bool sourceHasHashes = !string.IsNullOrWhiteSpace(sourceInfo.Sha1);
            if (!newInfo.IsSuccess
                || string.IsNullOrWhiteSpace(newInfo.Sha1)
                || (sourceHasHashes && !string.Equals(newInfo.Sha1, sourceInfo.Sha1, StringComparison.OrdinalIgnoreCase))
                || (sourceHasHashes && !string.Equals(newInfo.DataSha1, sourceInfo.DataSha1, StringComparison.OrdinalIgnoreCase)))
            {
                Log.Warning("Recompressed CHD does not match its source: {Source}", source);
                return Result(RecompressOutcome.Failed, HashMismatchKey);
            }

            long newBytes = TryGetLength(pendingPath);
            if (!IsWorthKeeping(originalBytes, newBytes))
            {
                return Result(RecompressOutcome.NoGain, NoGainKey, newBytes: newBytes);
            }

            if (!options.ReplaceOriginal)
            {
                File.Move(pendingPath, finalPath, overwrite: false);
                keepPending = true;
                return Result(RecompressOutcome.Saved, SavedKey, finalPath, newBytes);
            }

            // The lease must close before the source can be replaced. File.Replace swaps the files in one step
            // and keeps the old file as a backup until the swap has succeeded.
            lease.Dispose();
            string backupPath = pendingPath + ".bak";
            File.Replace(pendingPath, source, backupPath, ignoreMetadataErrors: true);
            keepPending = true;
            TryDelete(backupPath);
            return Result(RecompressOutcome.Replaced, ReplacedKey, source, newBytes);
        }
        catch (OperationCanceledException)
        {
            return Result(RecompressOutcome.Cancelled, CancelledKey);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or InvalidOperationException)
        {
            Log.Warning(ex, "Recompressing {Source} failed.", source);
            return Result(RecompressOutcome.Failed, FailedKey);
        }
        finally
        {
            lease.Dispose();
            if (!keepPending)
            {
                TryDelete(pendingPath);
            }
        }
    }

    /// <summary>A copy is kept only when it saves at least 1%; files already made with strong compression
    /// usually gain a few kilobytes, which is not worth a second file or a replacement.</summary>
    public static bool IsWorthKeeping(long originalBytes, long newBytes) =>
        newBytes > 0 && originalBytes > 0 && newBytes <= originalBytes - Math.Max(1, originalBytes / 100);

    private static long TryGetLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
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
            Log.Debug(ex, "Could not delete temporary recompress file {Path}.", path);
        }
    }
}
