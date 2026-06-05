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
            return BuildUnsupported(string.Empty, "No source path was provided.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedAnalysisException(ex))
        {
            return BuildUnsupported(path, "The source path is not valid.");
        }

        try
        {
            return _classifier.Classify(fullPath) switch
            {
                PS3InputFormat.Folder => _folderLayoutReader.Analyze(fullPath),
                PS3InputFormat.Iso => _isoLayoutReader.Analyze(fullPath),
                PS3InputFormat.Pkg => _pkgInspector.Analyze(fullPath),
                PS3InputFormat.Chd => AnalyzeChd(fullPath),
                _ => BuildUnsupported(fullPath, "The selected source is not a supported PS3 input format.")
            };
        }
        catch (Exception ex) when (IsExpectedAnalysisException(ex))
        {
            return BuildUnsupported(fullPath, "The selected source could not be analyzed safely.");
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
            warnings.Add("The selected CHD file was not found.");
        }
        else
        {
            warnings.Add("CHD input is recognized as an existing container. Use the CHD logical probe for geometry and compression details.");
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
                ? "CHD logical probe -> final report"
                : "Unsupported or missing CHD source",
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
        RecommendedPipeline: "Unsupported or incomplete PS3 source",
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