using HakamiqChdTool.App.Models.PlayStation;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3ContentIntakeService
{
    private readonly PS3ContentClassifier _classifier;
    private readonly PS3FolderLayoutReader _folderLayoutReader;
    private readonly PS3IsoLayoutReader _isoLayoutReader;
    private readonly PS3PkgInspector _pkgInspector;
    private readonly PS3ConversionPlanner _conversionPlanner;

    public PS3ContentIntakeService()
        : this(
            new PS3ContentClassifier(),
            new PS3FolderLayoutReader(),
            new PS3IsoLayoutReader(),
            new PS3PkgInspector(),
            new PS3ConversionPlanner())
    {
    }

    public PS3ContentIntakeService(
        PS3ContentClassifier classifier,
        PS3FolderLayoutReader folderLayoutReader,
        PS3IsoLayoutReader isoLayoutReader,
        PS3PkgInspector pkgInspector,
        PS3ConversionPlanner conversionPlanner)
    {
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(folderLayoutReader);
        ArgumentNullException.ThrowIfNull(isoLayoutReader);
        ArgumentNullException.ThrowIfNull(pkgInspector);
        ArgumentNullException.ThrowIfNull(conversionPlanner);

        _classifier = classifier;
        _folderLayoutReader = folderLayoutReader;
        _isoLayoutReader = isoLayoutReader;
        _pkgInspector = pkgInspector;
        _conversionPlanner = conversionPlanner;
    }

    public PS3ContentIntakeResult Analyze(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BuildUnsupported(string.Empty, PS3ContentIntakeMessages.WarningNoSourcePath);
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedAnalysisException(ex))
        {
            return BuildUnsupported(path, PS3ContentIntakeMessages.WarningSourcePathInvalid);
        }

        try
        {
            return _classifier.Classify(fullPath) switch
            {
                PS3InputFormat.Folder => _folderLayoutReader.Analyze(fullPath),
                PS3InputFormat.Iso => _isoLayoutReader.Analyze(fullPath),
                PS3InputFormat.Pkg => _pkgInspector.Analyze(fullPath),
                PS3InputFormat.Chd => AnalyzeChd(fullPath),
                _ => BuildUnsupported(fullPath, PS3ContentIntakeMessages.WarningUnsupportedInputFormat)
            };
        }
        catch (Exception ex) when (IsExpectedAnalysisException(ex))
        {
            return BuildUnsupported(fullPath, PS3ContentIntakeMessages.WarningAnalyzeUnsafe);
        }
    }

    public PS3ConversionPlan CreatePlan(PS3ContentIntakeResult input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return _conversionPlanner.CreatePlan(input);
    }

    public (PS3ContentIntakeResult Result, PS3ConversionPlan Plan) AnalyzeWithPlan(string path)
    {
        PS3ContentIntakeResult result = Analyze(path);
        return (result, _conversionPlanner.CreatePlan(result));
    }

    private static PS3ContentIntakeResult AnalyzeChd(string path)
    {
        bool exists = SafeFileExists(path);
        var warnings = new List<string>();

        if (!exists)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningChdMissing);
        }
        else
        {
            warnings.Add(PS3ContentIntakeMessages.WarningChdExistingContainer);
        }

        return new PS3ContentIntakeResult(
            InputFormat: PS3InputFormat.Chd,
            ContentKind: PS3ContentKind.Unknown,
            SourcePath: path,
            TitleId: null,
            TitleName: null,
            DiscId: null,
            HasPs3GameFolder: false,
            HasParamSfo: false,
            HasEbootBin: false,
            HasPs3DiscSfb: false,
            IsProbablyEncrypted: false,
            CanConvertToChd: false,
            RecommendedPipeline: exists
                ? PS3ContentIntakeMessages.PipelineChdLogicalReport
                : PS3ContentIntakeMessages.PipelineUnsupportedChd,
            Warnings: warnings);
    }

    private static PS3ContentIntakeResult BuildUnsupported(string path, string warning) => new(
        InputFormat: PS3InputFormat.Unknown,
        ContentKind: PS3ContentKind.Unknown,
        SourcePath: path,
        TitleId: null,
        TitleName: null,
        DiscId: null,
        HasPs3GameFolder: false,
        HasParamSfo: false,
        HasEbootBin: false,
        HasPs3DiscSfb: false,
        IsProbablyEncrypted: false,
        CanConvertToChd: false,
        RecommendedPipeline: PS3ContentIntakeMessages.PipelineUnsupportedSource,
        Warnings: [warning]);

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (IsExpectedAnalysisException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedAnalysisException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or IOException
        or InvalidDataException
        or OverflowException;
}