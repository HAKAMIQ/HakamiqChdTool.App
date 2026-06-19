using Serilog;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public enum CsoPreprocessStatus
{
    Success = 0,
    MissingTool = 1,
    Failed = 2,
    Cancelled = 3,
    Unsupported = 4
}

public interface ICsoPreprocessor
{
    Task<CsoToolProbeResult> InspectAsync(CancellationToken cancellationToken);

    Task<CsoPreprocessResult> PreprocessAsync(
        string inputCsoPath,
        string temporaryIsoPath,
        CancellationToken cancellationToken);
}

public sealed record CsoIntakeResult(
    bool Success,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    string ErrorCode,
    string ErrorMessage,
    int? HeaderVersion,
    long? HeaderUncompressedSize,
    long? BytesWritten);

public sealed record CsoPreprocessResult(
    bool IsSuccess,
    bool WasCancelled,
    string PreparedIsoPath,
    string MessageKey,
    string StandardOutput,
    string StandardError,
    int ExitCode,
    long? PreparedIsoBytes,
    string ToolName,
    string ToolPath,
    string CommandDisplay,
    CsoPreprocessStatus Status,
    string ToolVersion,
    CsoIntakeResult? InfoResult = null,
    CsoIntakeResult? VerifyResult = null,
    CsoIntakeResult? DecompressResult = null,
    long? EstimatedIsoBytes = null,
    long? AvailableTempBytes = null);

public sealed class CsoPreprocessor : ICsoPreprocessor
{
    public const string ToolName = "Hakamiq CsoKit";
    public const string ToolExecutableName = "hakamiq-cso.exe";
    public const string ToolMissingMessageKey = "LocWorkflow_CsoToolMissing";
    public const string ToolFailedMessageKey = "LocWorkflow_CsoToolFailed";
    public const string PreparationFailedMessageKey = "LocWorkflow_CsoPreparationFailed";
    public const string PreparationCancelledMessageKey = "LocWorkflow_CsoPreparationCancelled";
    public const string PreparedMessageKey = "LocWorkflow_CsoPrepared";
    public const string UnsupportedMessageKey = "LocWorkflow_CsoUnsupported";
    public const string TempSpaceInsufficientMessageKey = "LocWorkflow_CsoTempSpaceInsufficient";

    private const int ExitSuccess = 0;
    private const int ExitInputNotFound = 10;
    private const int ExitUnsupportedCsoVersion = 12;
    private const int ExitNotEnoughDiskSpace = 16;
    private const int ExitOperationCanceled = 130;
    private const long TempSpaceSafetyBytes = 64L * 1024L * 1024L;

    private static readonly ILogger Log = global::Serilog.Log.ForContext<CsoPreprocessor>();

    private readonly CsoToolProbe _probe;
    private readonly ExternalToolProcessRunner _runner;

    public CsoPreprocessor()
        : this(new CsoToolProbe(), new ExternalToolProcessRunner())
    {
    }

    public CsoPreprocessor(CsoToolProbe probe, ExternalToolProcessRunner runner)
    {
        _probe = probe ?? throw new ArgumentNullException(nameof(probe));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public Task<CsoToolProbeResult> InspectAsync(CancellationToken cancellationToken) =>
        _probe.CheckAsync(cancellationToken);

    public async Task<CsoPreprocessResult> PreprocessAsync(
        string inputCsoPath,
        string temporaryIsoPath,
        CancellationToken cancellationToken)
    {
        string inputPath = NormalizeExistingCsoPath(inputCsoPath);
        string outputPath = NormalizeTemporaryIsoPath(temporaryIsoPath);
        EnsureTemporaryOutputReady(outputPath);

        CsoToolProbeResult tool = await InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!tool.IsAvailable)
        {
            return ToolUnavailable(outputPath, tool);
        }

        CsoIntakeResult info = await RunJsonCommandAsync(
                tool.ToolPath,
                ["info", inputPath, "--json"],
                cancellationToken)
            .ConfigureAwait(false);

        if (IsCancelled(info))
        {
            TryDeleteTemporaryOutput(outputPath, "CSO info cancelled");
            return BuildResult(false, true, outputPath, PreparationCancelledMessageKey, info, null, null, null, tool, string.Empty, CsoPreprocessStatus.Cancelled);
        }

        if (!info.Success || info.ExitCode != ExitSuccess)
        {
            string messageKey = IsUnsupported(info)
                ? UnsupportedMessageKey
                : PreparationFailedMessageKey;
            CsoPreprocessStatus status = IsUnsupported(info)
                ? CsoPreprocessStatus.Unsupported
                : CsoPreprocessStatus.Failed;
            return BuildResult(false, false, outputPath, messageKey, info, null, null, null, tool, string.Empty, status);
        }

        if (info.HeaderVersion is not 1)
        {
            return BuildResult(false, false, outputPath, UnsupportedMessageKey, info, null, null, null, tool, string.Empty, CsoPreprocessStatus.Unsupported);
        }

        CsoIntakeResult verify = await RunJsonCommandAsync(
                tool.ToolPath,
                ["verify", inputPath, "--json"],
                cancellationToken)
            .ConfigureAwait(false);

        if (IsCancelled(verify))
        {
            TryDeleteTemporaryOutput(outputPath, "CSO verify cancelled");
            return BuildResult(false, true, outputPath, PreparationCancelledMessageKey, verify, info, verify, null, tool, string.Empty, CsoPreprocessStatus.Cancelled);
        }

        if (!verify.Success || verify.ExitCode != ExitSuccess)
        {
            string messageKey = IsUnsupported(verify)
                ? UnsupportedMessageKey
                : PreparationFailedMessageKey;
            CsoPreprocessStatus status = IsUnsupported(verify)
                ? CsoPreprocessStatus.Unsupported
                : CsoPreprocessStatus.Failed;
            return BuildResult(false, false, outputPath, messageKey, verify, info, verify, null, tool, string.Empty, status);
        }

        CsoTempSpaceCheck tempSpace = CheckTemporaryStorage(outputPath, info.HeaderUncompressedSize);
        if (!tempSpace.CanContinue)
        {
            return new CsoPreprocessResult(
                false,
                false,
                outputPath,
                TempSpaceInsufficientMessageKey,
                string.Empty,
                tempSpace.Detail,
                ExitNotEnoughDiskSpace,
                null,
                ToolName,
                tool.ToolPath,
                string.Empty,
                CsoPreprocessStatus.Failed,
                tool.VersionText,
                info,
                verify,
                null,
                tempSpace.EstimatedIsoBytes,
                tempSpace.AvailableFreeBytes);
        }

        string[] decompressArguments = ["decompress", inputPath, "-o", outputPath, "--force", "--json"];
        string displayCommand = FormatCommandSequence(tool.ToolPath, inputPath, outputPath);

        Log.Information(
            "CSO preprocessing starting. Tool={ToolPath}; Version={Version}; Input={Input}; PreparedIso={PreparedIso}; EstimatedIsoBytes={EstimatedIsoBytes}; AvailableTempBytes={AvailableTempBytes}",
            tool.ToolPath,
            tool.VersionText,
            inputPath,
            outputPath,
            tempSpace.EstimatedIsoBytes,
            tempSpace.AvailableFreeBytes);

        CsoIntakeResult decompress = await RunJsonCommandAsync(
                tool.ToolPath,
                decompressArguments,
                cancellationToken)
            .ConfigureAwait(false);

        if (IsCancelled(decompress))
        {
            TryDeleteTemporaryOutput(outputPath, "CSO decompression cancelled");
            return BuildResult(false, true, outputPath, PreparationCancelledMessageKey, decompress, info, verify, decompress, tool, displayCommand, CsoPreprocessStatus.Cancelled, tempSpace);
        }

        if (decompress.ExitCode != ExitSuccess
            || !decompress.Success
            || !TryGetPreparedIsoLength(outputPath, out long preparedBytes)
            || preparedBytes <= 0)
        {
            TryDeleteTemporaryOutput(outputPath, "CSO decompression failed");

            string messageKey = IsUnsupported(decompress)
                ? UnsupportedMessageKey
                : decompress.ExitCode == ExitNotEnoughDiskSpace
                    ? TempSpaceInsufficientMessageKey
                    : PreparationFailedMessageKey;

            CsoPreprocessStatus status = IsUnsupported(decompress)
                ? CsoPreprocessStatus.Unsupported
                : CsoPreprocessStatus.Failed;

            Log.Warning(
                "CSO preprocessing failed. Tool={ToolPath}; Input={Input}; PreparedIso={PreparedIso}; ExitCode={ExitCode}; ErrorCode={ErrorCode}",
                tool.ToolPath,
                inputPath,
                outputPath,
                decompress.ExitCode,
                decompress.ErrorCode);

            return BuildResult(false, false, outputPath, messageKey, decompress, info, verify, decompress, tool, displayCommand, status, tempSpace);
        }

        Log.Information(
            "CSO preprocessing completed. Tool={ToolPath}; Version={Version}; Input={Input}; PreparedIso={PreparedIso}; PreparedBytes={PreparedBytes}",
            tool.ToolPath,
            tool.VersionText,
            inputPath,
            outputPath,
            preparedBytes);

        return new CsoPreprocessResult(
            true,
            false,
            outputPath,
            PreparedMessageKey,
            decompress.StandardOutput,
            decompress.StandardError,
            decompress.ExitCode,
            preparedBytes,
            ToolName,
            tool.ToolPath,
            displayCommand,
            CsoPreprocessStatus.Success,
            tool.VersionText,
            info,
            verify,
            decompress,
            tempSpace.EstimatedIsoBytes,
            tempSpace.AvailableFreeBytes);
    }

    private async Task<CsoIntakeResult> RunJsonCommandAsync(
        string toolPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        ExternalToolProcessResult process = await _runner
            .RunAsync(toolPath, arguments, cancellationToken)
            .ConfigureAwait(false);

        CsoJsonPayload payload = ParseJsonPayload(process.StandardOutput);

        return new CsoIntakeResult(
            payload.Success,
            process.WasCancelled ? ExitOperationCanceled : process.ExitCode,
            process.StandardOutput,
            process.StandardError,
            payload.ErrorCode,
            payload.ErrorMessage,
            payload.HeaderVersion,
            payload.HeaderUncompressedSize,
            payload.BytesWritten);
    }

    private static CsoJsonPayload ParseJsonPayload(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return CsoJsonPayload.Empty;
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            bool success = TryReadBool(root, "success") ?? false;
            int? headerVersion = null;
            long? headerUncompressedSize = null;
            long? bytesWritten = TryReadInt64(root, "bytesWritten");
            string errorCode = string.Empty;
            string errorMessage = string.Empty;

            if (root.TryGetProperty("header", out JsonElement header)
                && header.ValueKind == JsonValueKind.Object)
            {
                headerVersion = TryReadInt32(header, "version");
                headerUncompressedSize = TryReadInt64(header, "uncompressedSize");
            }

            if (root.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.Object)
            {
                errorCode = TryReadString(error, "code");
                errorMessage = TryReadString(error, "message");
            }
            else if (root.TryGetProperty("issues", out JsonElement issues)
                     && issues.ValueKind == JsonValueKind.Array
                     && issues.GetArrayLength() > 0)
            {
                JsonElement firstIssue = issues[0];
                if (firstIssue.ValueKind == JsonValueKind.Object)
                {
                    errorCode = TryReadString(firstIssue, "code");
                    errorMessage = TryReadString(firstIssue, "message");
                }
            }

            return new CsoJsonPayload(
                success,
                errorCode,
                errorMessage,
                headerVersion,
                headerUncompressedSize,
                bytesWritten);
        }
        catch (JsonException)
        {
            return CsoJsonPayload.Empty;
        }
    }

    private static bool IsCancelled(CsoIntakeResult result) =>
        result.ExitCode == ExitOperationCanceled;

    private static bool IsUnsupported(CsoIntakeResult result) =>
        result.ExitCode == ExitUnsupportedCsoVersion
        || string.Equals(result.ErrorCode, "UnsupportedVersion", StringComparison.OrdinalIgnoreCase)
        || string.Equals(result.ErrorCode, "UnsupportedCsoVersion", StringComparison.OrdinalIgnoreCase)
        || string.Equals(result.ErrorCode, "UnsupportedDecompressionVersion", StringComparison.OrdinalIgnoreCase);

    private static CsoPreprocessResult ToolUnavailable(
        string outputPath,
        CsoToolProbeResult tool)
    {
        CsoPreprocessStatus status = tool.Status == CsoToolStatus.Missing
            ? CsoPreprocessStatus.MissingTool
            : CsoPreprocessStatus.Failed;

        string messageKey = status == CsoPreprocessStatus.MissingTool
            ? ToolMissingMessageKey
            : ToolFailedMessageKey;

        return new CsoPreprocessResult(
            false,
            false,
            outputPath,
            messageKey,
            tool.DiagnosticText,
            string.Empty,
            tool.ExitCode ?? 1,
            null,
            ToolName,
            tool.ToolPath,
            string.Empty,
            status,
            tool.VersionText);
    }

    private static CsoPreprocessResult BuildResult(
        bool isSuccess,
        bool wasCancelled,
        string outputPath,
        string messageKey,
        CsoIntakeResult processResult,
        CsoIntakeResult? info,
        CsoIntakeResult? verify,
        CsoIntakeResult? decompress,
        CsoToolProbeResult tool,
        string displayCommand,
        CsoPreprocessStatus status,
        CsoTempSpaceCheck? tempSpace = null) => new(
            isSuccess,
            wasCancelled,
            outputPath,
            messageKey,
            processResult.StandardOutput,
            processResult.StandardError,
            processResult.ExitCode,
            decompress?.BytesWritten,
            ToolName,
            tool.ToolPath,
            displayCommand,
            status,
            tool.VersionText,
            info,
            verify,
            decompress,
            tempSpace?.EstimatedIsoBytes,
            tempSpace?.AvailableFreeBytes);

    private static CsoTempSpaceCheck CheckTemporaryStorage(string outputPath, long? estimatedIsoBytes)
    {
        try
        {
            string directory = Path.GetDirectoryName(outputPath) ?? AppPaths.ProcessTempRoot;
            Directory.CreateDirectory(directory);
            string root = Path.GetPathRoot(Path.GetFullPath(directory)) ?? directory;
            DriveInfo drive = new(root);
            long available = drive.AvailableFreeSpace;
            long required = checked(Math.Max(estimatedIsoBytes ?? 0, 0) + TempSpaceSafetyBytes);

            return available >= required
                ? CsoTempSpaceCheck.Ok(estimatedIsoBytes, available)
                : CsoTempSpaceCheck.Fail(estimatedIsoBytes, available, $"Required={required.ToString(CultureInfo.InvariantCulture)}; Available={available.ToString(CultureInfo.InvariantCulture)}");
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            return CsoTempSpaceCheck.Fail(estimatedIsoBytes, null, ex.Message);
        }
    }

    private static string NormalizeExistingCsoPath(string inputCsoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputCsoPath);

        string fullPath = Path.GetFullPath(inputCsoPath.Trim());
        ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(inputCsoPath));

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("LocConversion_InputFileNotFound", fullPath);
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".cso", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(PreparationFailedMessageKey);
        }

        return fullPath;
    }

    private static string NormalizeTemporaryIsoPath(string temporaryIsoPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(temporaryIsoPath);

        string fullPath = Path.GetFullPath(temporaryIsoPath.Trim());
        ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(temporaryIsoPath));

        if (!string.Equals(Path.GetExtension(fullPath), ".iso", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(PreparationFailedMessageKey);
        }

        if (!AppPaths.IsPathUnderProcessTempRoot(fullPath))
        {
            throw new InvalidOperationException("LocAppPaths_OutsideProcessTempRoot");
        }

        return fullPath;
    }

    private static void EnsureTemporaryOutputReady(string outputPath)
    {
        string? directory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException(PreparationFailedMessageKey);
        }

        if (!AppPaths.IsPathUnderProcessTempRoot(directory))
        {
            throw new InvalidOperationException("LocAppPaths_OutsideProcessTempRoot");
        }

        Directory.CreateDirectory(directory);

        if (File.Exists(outputPath))
        {
            TryDeleteTemporaryOutput(outputPath, "stale CSO preprocessing output");
        }
    }

    private static bool TryGetPreparedIsoLength(string outputPath, out long length)
    {
        length = 0;

        try
        {
            FileInfo info = new(outputPath);
            if (!info.Exists)
            {
                return false;
            }

            length = info.Length;
            return true;
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException)
        {
            return false;
        }
    }

    private static void TryDeleteTemporaryOutput(string outputPath, string reason)
    {
        try
        {
            string fullPath = Path.GetFullPath(outputPath);
            if (!AppPaths.IsPathUnderProcessTempRoot(fullPath) || !File.Exists(fullPath))
            {
                return;
            }

            File.Delete(fullPath);
            Log.Information("Deleted temporary CSO preprocessing output. Path={Path}; Reason={Reason}", fullPath, reason);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            Log.Debug(ex, "Could not delete temporary CSO preprocessing output. Path={Path}; Reason={Reason}", outputPath, reason);
        }
    }

    private static string FormatCommandSequence(string executablePath, string inputPath, string outputPath)
    {
        string info = ChdmanCliRunner.FormatCommandLineForDisplay(executablePath, ["info", inputPath, "--json"]);
        string verify = ChdmanCliRunner.FormatCommandLineForDisplay(executablePath, ["verify", inputPath, "--json"]);
        string decompress = ChdmanCliRunner.FormatCommandLineForDisplay(executablePath, ["decompress", inputPath, "-o", outputPath, "--force", "--json"]);
        return string.Join(Environment.NewLine, info, verify, decompress);
    }

    private static bool? TryReadBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static int? TryReadInt32(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        return value.TryGetInt32(out int result) ? result : null;
    }

    private static long? TryReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        if (value.TryGetInt64(out long signed))
        {
            return signed;
        }

        if (value.TryGetUInt64(out ulong unsigned))
        {
            return unsigned > long.MaxValue ? long.MaxValue : (long)unsigned;
        }

        return null;
    }

    private static string TryReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private sealed record CsoJsonPayload(
        bool Success,
        string ErrorCode,
        string ErrorMessage,
        int? HeaderVersion,
        long? HeaderUncompressedSize,
        long? BytesWritten)
    {
        public static CsoJsonPayload Empty { get; } = new(false, string.Empty, string.Empty, null, null, null);
    }

    private sealed record CsoTempSpaceCheck(
        bool CanContinue,
        long? EstimatedIsoBytes,
        long? AvailableFreeBytes,
        string Detail)
    {
        public static CsoTempSpaceCheck Ok(long? estimatedIsoBytes, long availableFreeBytes) =>
            new(true, estimatedIsoBytes, availableFreeBytes, string.Empty);

        public static CsoTempSpaceCheck Fail(long? estimatedIsoBytes, long? availableFreeBytes, string detail) =>
            new(false, estimatedIsoBytes, availableFreeBytes, detail);
    }
}
