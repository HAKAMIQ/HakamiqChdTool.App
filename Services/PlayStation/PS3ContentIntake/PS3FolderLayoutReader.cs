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
            warnings.Add("The selected folder path is not valid.");
            return BuildUnsupported(path, warnings);
        }

        if (!SafeDirectoryExists(fullPath))
        {
            warnings.Add("The selected folder was not found.");
            return BuildUnsupported(fullPath, warnings);
        }

        if (IsReparsePoint(fullPath))
        {
            warnings.Add("The selected folder is a reparse point and cannot be analyzed safely.");
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
            warnings.Add("PS3_GAME is a reparse point and was not followed.");
        }

        bool hasParam = hasPs3Game && SafeFileExists(paramSfo);
        bool hasEboot = hasPs3Game && SafeFileExists(eboot);
        bool hasDiscSfb = SafeFileExists(discSfb);

        if (!hasPs3Game)
        {
            warnings.Add("The folder does not contain a safe PS3_GAME directory.");
        }

        if (hasPs3Game && !hasParam)
        {
            warnings.Add("PARAM.SFO was not found inside PS3_GAME.");
        }

        if (hasPs3Game && !hasEboot)
        {
            warnings.Add("EBOOT.BIN was not found inside PS3_GAME/USRDIR.");
        }

        if (hasPs3Game && !hasDiscSfb)
        {
            warnings.Add("PS3_DISC.SFB was not found. This may be extracted content, but disc identity is weaker.");
        }

        if (selectedPs3GameFolder)
        {
            warnings.Add("The selected folder is PS3_GAME itself. The parent folder was used for disc metadata checks.");
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
                ? "Folder -> temporary ISO -> chdman createdvd -> CHD"
                : "Unsupported folder layout",
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
        RecommendedPipeline: "Unsupported folder layout",
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