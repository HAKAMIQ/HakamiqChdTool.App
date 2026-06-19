using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Intake;

public sealed class IntakeAnalysisService
{
    private const string SourceName = nameof(IntakeAnalysisService);
    private const string PathMissingMessageKey = "LocIntake_PathMissing";

    private readonly IntakeAnalyzer _analyzer;

    public IntakeAnalysisService()
        : this(new IntakeAnalyzer())
    {
    }

    public IntakeAnalysisService(IntakeAnalyzer analyzer)
    {
        ArgumentNullException.ThrowIfNull(analyzer);

        _analyzer = analyzer;
    }

    public Task<IntakeDecision> AnalyzeAsync(
        string inputPath,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);

        IntakeContext context = BuildContext(inputPath);

        cancellationToken.ThrowIfCancellationRequested();

        IntakeDecision decision = _analyzer.Analyze(context);

        foreach (IntakeDecisionReason reason in context.ProbeReasons)
        {
            decision = decision.WithReason(reason);
        }

        foreach (IntakeDecisionReason warning in context.ProbeWarnings)
        {
            decision = decision.WithWarning(warning);
        }

        return Task.FromResult(decision);
    }

    private static IntakeContext BuildContext(string inputPath)
    {
        bool isDirectory = Directory.Exists(inputPath);
        bool isFile = File.Exists(inputPath);

        if (!isDirectory && !isFile)
        {
            return IntakeContext.Missing(inputPath).WithProbeWarning(CreateWarning(
                "INTAKE_PATH_MISSING",
                PathMissingMessageKey));
        }

        string fileName;
        string suggestedCleanName;
        string extension;

        if (isDirectory)
        {
            DirectoryInfo directory = new(inputPath);
            fileName = directory.Name;
            suggestedCleanName = directory.Name;
            extension = string.Empty;
        }
        else
        {
            fileName = Path.GetFileName(inputPath);
            suggestedCleanName = Path.GetFileNameWithoutExtension(inputPath);
            extension = Path.GetExtension(inputPath);
        }

        string normalizedExtension = extension.TrimStart('.').ToLowerInvariant();

        return new IntakeContext
        {
            InputPath = inputPath,
            FileName = fileName,
            Extension = extension,
            Exists = true,
            IsDirectory = isDirectory,
            IsArchive = IsArchiveExtension(normalizedExtension),
            IsChd = string.Equals(normalizedExtension, "chd", StringComparison.OrdinalIgnoreCase),
            IsDiscImage = IsDiscImageExtension(normalizedExtension),
            IsCueLikeDescriptor = IsCueLikeDescriptorExtension(normalizedExtension),
            IsUnsupportedDiscImage = IsUnsupportedDiscImageExtension(normalizedExtension),
            SuggestedCleanName = suggestedCleanName
        };
    }

    private static bool IsArchiveExtension(string extension)
    {
        return extension is "zip" or "rar" or "7z";
    }

    private static bool IsDiscImageExtension(string extension)
    {
        return extension is "iso" or "cue" or "gdi" or "toc" or "nrg" or "bin" or "cso";
    }

    private static bool IsCueLikeDescriptorExtension(string extension)
    {
        return extension is "cue" or "gdi" or "toc";
    }

    private static bool IsUnsupportedDiscImageExtension(string extension)
    {
        return extension is "cdi";
    }

    private static IntakeDecisionReason CreateWarning(string code, string messageKey)
    {
        return new IntakeDecisionReason(
            code,
            messageKey,
            IntakeDecisionSeverity.Warning,
            SourceName);
    }
}
