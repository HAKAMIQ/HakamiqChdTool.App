using HakamiqChdTool.App.Models.PlayStation;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3FolderLayoutReader
{
    private readonly PS3ParamSfoReader _paramSfoReader;
    private readonly PS3DiscSfbReader _discSfbReader;

    public PS3FolderLayoutReader()
        : this(new PS3ParamSfoReader(), new PS3DiscSfbReader())
    {
    }

    public PS3FolderLayoutReader(PS3ParamSfoReader paramSfoReader, PS3DiscSfbReader discSfbReader)
    {
        ArgumentNullException.ThrowIfNull(paramSfoReader);
        ArgumentNullException.ThrowIfNull(discSfbReader);

        _paramSfoReader = paramSfoReader;
        _discSfbReader = discSfbReader;
    }

    public PS3ContentIntakeResult Analyze(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var warnings = new List<string>();

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderInvalidPath);
            return BuildUnsupported(path, warnings);
        }

        if (!SafeDirectoryExists(fullPath))
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderMissing);
            return BuildUnsupported(fullPath, warnings);
        }

        if (IsReparsePoint(fullPath))
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderReparsePoint);
            return BuildUnsupported(fullPath, warnings);
        }

        string trimmedPath = Path.TrimEndingDirectorySeparator(fullPath);
        bool selectedPs3GameFolder = string.Equals(
            Path.GetFileName(trimmedPath),
            "PS3_GAME",
            StringComparison.OrdinalIgnoreCase);

        string ps3Game = selectedPs3GameFolder ? fullPath : Path.Combine(fullPath, "PS3_GAME");
        string discRoot = selectedPs3GameFolder
            ? Path.GetDirectoryName(trimmedPath) ?? fullPath
            : fullPath;

        string paramSfo = Path.Combine(ps3Game, "PARAM.SFO");
        string eboot = Path.Combine(ps3Game, "USRDIR", "EBOOT.BIN");
        string discSfb = Path.Combine(discRoot, "PS3_DISC.SFB");

        bool ps3GameExists = SafeDirectoryExists(ps3Game);
        bool ps3GameIsReparsePoint = ps3GameExists && IsReparsePoint(ps3Game);
        bool hasPs3Game = ps3GameExists && !ps3GameIsReparsePoint;

        if (ps3GameIsReparsePoint)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningPs3GameReparsePoint);
        }

        bool hasParam = hasPs3Game && SafeFileExists(paramSfo);
        bool hasEboot = hasPs3Game && SafeFileExists(eboot);
        bool hasDiscSfb = SafeFileExists(discSfb);

        if (!hasPs3Game)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderNoSafePs3Game);
        }

        if (hasPs3Game && !hasParam)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderParamMissing);
        }

        if (hasPs3Game && !hasEboot)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderEbootMissing);
        }

        if (hasPs3Game && !hasDiscSfb)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningFolderDiscSfbMissing);
        }

        if (selectedPs3GameFolder)
        {
            warnings.Add(PS3ContentIntakeMessages.WarningSelectedFolderIsPs3Game);
        }

        PS3ContentIdentity identity = hasParam
            ? _paramSfoReader.ReadFromFile(paramSfo)
            : PS3ContentIdentity.Empty;

        string? discId = hasDiscSfb
            ? _discSfbReader.ReadDiscIdFromFile(discSfb)
            : null;

        string? titleId = FirstNonEmpty(identity.TitleId, discId);
        bool canConvert = hasPs3Game && hasParam && hasEboot;

        PS3ContentKind contentKind = hasDiscSfb
            ? PS3ContentKind.DiscGame
            : InferContentKind(identity.Category);

        return new PS3ContentIntakeResult(
            InputFormat: PS3InputFormat.Folder,
            ContentKind: contentKind,
            SourcePath: fullPath,
            TitleId: titleId,
            TitleName: identity.TitleName,
            DiscId: discId,
            HasPs3GameFolder: hasPs3Game,
            HasParamSfo: hasParam,
            HasEbootBin: hasEboot,
            HasPs3DiscSfb: hasDiscSfb,
            IsProbablyEncrypted: false,
            CanConvertToChd: canConvert,
            RecommendedPipeline: canConvert
                ? PS3ContentIntakeMessages.PipelineFolderToChd
                : PS3ContentIntakeMessages.PipelineUnsupportedFolder,
            Warnings: warnings);
    }

    private static PS3ContentIntakeResult BuildUnsupported(string path, IReadOnlyList<string> warnings) => new(
        InputFormat: PS3InputFormat.Folder,
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
        RecommendedPipeline: PS3ContentIntakeMessages.PipelineUnsupportedFolder,
        Warnings: warnings);

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool SafeFileExists(string path)
    {
        try
        {
            return File.Exists(path) && !IsReparsePoint(path);
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path))
            {
                return false;
            }

            FileAttributes attributes = File.GetAttributes(path);
            return (attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return true;
        }
    }

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static PS3ContentKind InferContentKind(string? category)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            return PS3ContentKind.Unknown;
        }

        return category.Trim().ToUpperInvariant() switch
        {
            "DG" => PS3ContentKind.DiscGame,
            "HG" => PS3ContentKind.PsnGame,
            "GD" => PS3ContentKind.GameUpdate,
            "AP" or "HM" or "CB" => PS3ContentKind.Application,
            _ => PS3ContentKind.Unknown
        };
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or UnauthorizedAccessException
        or IOException;
}