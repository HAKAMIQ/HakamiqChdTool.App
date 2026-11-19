using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

/// <summary>
/// ZSO (LZ4 compressed ISO) preparation. CsoKit 0.6.4+ can decompress ZSO, but
/// its <c>info</c> command and shallow <c>verify</c> are CSO-only, so the ZSO path
/// reads the header with <c>detect</c>, lets <c>decompress</c> fail closed on
/// damaged data, and then requires the ISO size to equal the declared size.
/// </summary>
public sealed partial class CsoPreprocessor
{
    public const string ZsoToolTooOldMessageKey = "LocWorkflow_ZsoToolTooOld";
    public const string ZsoPreparationFailedMessageKey = "LocWorkflow_ZsoPreparationFailed";
    public const string ZsoPreparationCancelledMessageKey = "LocWorkflow_ZsoPreparationCancelled";
    public const string ZsoPreparedMessageKey = "LocWorkflow_ZsoPrepared";
    public const string ZsoTempSpaceInsufficientMessageKey = "LocWorkflow_ZsoTempSpaceInsufficient";

    private static readonly Version MinimumZsoToolVersion = new(0, 6, 4);

    private static readonly Regex ToolVersionPattern = new(
        @"(?<version>\d+\.\d+\.\d+)",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static bool IsZsoPath(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && string.Equals(Path.GetExtension(path.Trim()), ".zso", StringComparison.OrdinalIgnoreCase);

    internal static bool SupportsZso(string? toolVersionText)
    {
        if (string.IsNullOrWhiteSpace(toolVersionText))
        {
            return false;
        }

        Match match = ToolVersionPattern.Match(toolVersionText);
        return match.Success
            && Version.TryParse(match.Groups["version"].Value, out Version? version)
            && version >= MinimumZsoToolVersion;
    }

    private async Task<CsoPreprocessResult> PreprocessZsoAsync(
        string inputZsoPath,
        string temporaryIsoPath,
        CancellationToken cancellationToken)
    {
        string inputPath = NormalizeExistingZsoPath(inputZsoPath);
        string outputPath = NormalizeTemporaryIsoPath(temporaryIsoPath);
        EnsureTemporaryOutputReady(outputPath);

        CsoToolProbeResult tool = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tool.IsAvailable)
        {
            return ToolUnavailable(outputPath, tool);
        }

        if (!SupportsZso(tool.VersionText))
        {
            Log.Warning(
                "ZSO preprocessing needs CsoKit {MinimumVersion} or newer. Tool={ToolPath}; Version={Version}",
                MinimumZsoToolVersion,
                tool.ToolPath,
                tool.VersionText);

            return new CsoPreprocessResult(
                false,
                false,
                outputPath,
                ZsoToolTooOldMessageKey,
                string.Empty,
                tool.VersionText,
                1,
                null,
                ToolName,
                tool.ToolPath,
                string.Empty,
                CsoPreprocessStatus.Unsupported,
                tool.VersionText);
        }

        CsoIntakeResult detect = await RunJsonCommandAsync(
                tool.ToolPath,
                ["detect", inputPath, "--json"],
                cancellationToken)
            .ConfigureAwait(false);

        if (IsCancelled(detect))
        {
            TryDeleteTemporaryOutput(outputPath, "ZSO detect cancelled");
            return BuildResult(false, true, outputPath, ZsoPreparationCancelledMessageKey, detect, detect, null, null, tool, string.Empty, CsoPreprocessStatus.Cancelled);
        }

        long? declaredBytes = TryReadZsoUncompressedSize(detect.StandardOutput);
        if (!detect.Success || detect.ExitCode != ExitSuccess || declaredBytes is not > 0)
        {
            return BuildResult(false, false, outputPath, ZsoPreparationFailedMessageKey, detect, detect, null, null, tool, string.Empty, CsoPreprocessStatus.Failed);
        }

        CsoTempSpaceCheck tempSpace = CheckTemporaryStorage(outputPath, declaredBytes);
        if (!tempSpace.CanContinue)
        {
            return new CsoPreprocessResult(
                false,
                false,
                outputPath,
                ZsoTempSpaceInsufficientMessageKey,
                string.Empty,
                tempSpace.Detail,
                ExitNotEnoughDiskSpace,
                null,
                ToolName,
                tool.ToolPath,
                string.Empty,
                CsoPreprocessStatus.Failed,
                tool.VersionText,
                detect,
                null,
                null,
                tempSpace.EstimatedIsoBytes,
                tempSpace.AvailableFreeBytes);
        }

        string[] decompressArguments = ["decompress", inputPath, "-o", outputPath, "--force", "--json"];
        string displayCommand = string.Join(
            Environment.NewLine,
            ChdmanCliRunner.FormatCommandLineForDisplay(tool.ToolPath, ["detect", inputPath, "--json"]),
            ChdmanCliRunner.FormatCommandLineForDisplay(tool.ToolPath, decompressArguments));

        Log.Information(
            "ZSO preprocessing starting. Tool={ToolPath}; Version={Version}; Input={Input}; PreparedIso={PreparedIso}; DeclaredBytes={DeclaredBytes}",
            tool.ToolPath,
            tool.VersionText,
            inputPath,
            outputPath,
            declaredBytes);

        CsoIntakeResult decompress = await RunJsonCommandAsync(
                tool.ToolPath,
                decompressArguments,
                cancellationToken)
            .ConfigureAwait(false);

        if (IsCancelled(decompress))
        {
            TryDeleteTemporaryOutput(outputPath, "ZSO decompression cancelled");
            return BuildResult(false, true, outputPath, ZsoPreparationCancelledMessageKey, decompress, detect, null, decompress, tool, displayCommand, CsoPreprocessStatus.Cancelled, tempSpace);
        }

        bool prepared = decompress.ExitCode == ExitSuccess
            && decompress.Success
            && TryGetPreparedIsoLength(outputPath, out long preparedBytes)
            && preparedBytes == declaredBytes.Value;

        if (!prepared)
        {
            TryDeleteTemporaryOutput(outputPath, "ZSO decompression failed or size mismatch");

            Log.Warning(
                "ZSO preprocessing failed. Tool={ToolPath}; Input={Input}; PreparedIso={PreparedIso}; ExitCode={ExitCode}; ErrorCode={ErrorCode}; DeclaredBytes={DeclaredBytes}",
                tool.ToolPath,
                inputPath,
                outputPath,
                decompress.ExitCode,
                decompress.ErrorCode,
                declaredBytes);

            string messageKey = decompress.ExitCode == ExitNotEnoughDiskSpace
                ? ZsoTempSpaceInsufficientMessageKey
                : ZsoPreparationFailedMessageKey;

            return BuildResult(false, false, outputPath, messageKey, decompress, detect, null, decompress, tool, displayCommand, CsoPreprocessStatus.Failed, tempSpace);
        }

        Log.Information(
            "ZSO preprocessing completed. Tool={ToolPath}; Version={Version}; Input={Input}; PreparedIso={PreparedIso}; PreparedBytes={PreparedBytes}",
            tool.ToolPath,
            tool.VersionText,
            inputPath,
            outputPath,
            declaredBytes);

        return new CsoPreprocessResult(
            true,
            false,
            outputPath,
            ZsoPreparedMessageKey,
            decompress.StandardOutput,
            decompress.StandardError,
            decompress.ExitCode,
            declaredBytes,
            ToolName,
            tool.ToolPath,
            displayCommand,
            CsoPreprocessStatus.Success,
            tool.VersionText,
            detect,
            null,
            decompress,
            tempSpace.EstimatedIsoBytes,
            tempSpace.AvailableFreeBytes);
    }

    private static string NormalizeExistingZsoPath(string inputZsoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputZsoPath);

        string fullPath = Path.GetFullPath(inputZsoPath.Trim());
        ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(inputZsoPath));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("LocConversion_InputFileNotFound", fullPath);
        }

        if (!IsZsoPath(fullPath))
        {
            throw new InvalidOperationException(ZsoPreparationFailedMessageKey);
        }

        return fullPath;
    }

    /// <summary>Reads the declared ISO size from <c>csokit detect --json</c>; only a ZSO result counts.</summary>
    internal static long? TryReadZsoUncompressedSize(string? detectJson)
    {
        if (string.IsNullOrWhiteSpace(detectJson))
        {
            return null;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(detectJson);
            JsonElement root = document.RootElement;

            if (!string.Equals(TryReadString(root, "format"), "Zso", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(TryReadString(root, "magic"), "ZISO", StringComparison.Ordinal))
            {
                return null;
            }

            return TryReadInt64(root, "uncompressedSize");
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
