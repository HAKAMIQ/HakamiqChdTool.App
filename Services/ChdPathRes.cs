using HakamiqChdTool.App.Models;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed class ChdmanPathResolver
{
    private readonly RuntimeToolService _runtimeTools;

    public ChdmanPathResolver(RuntimeToolService runtimeTools)
    {
        _runtimeTools = runtimeTools ?? throw new ArgumentNullException(nameof(runtimeTools));
    }

    public string ResolvePath(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.UseBundledChdman)
        {
            if (TryResolveExternal(settings.ExternalChdmanPath, out string? externalPath))
            {
                return externalPath;
            }

            // The user chose their own chdman; say so when it cannot be used rather than switching silently.
            Serilog.Log.Warning(
                "External chdman is not usable; the bundled chdman is used instead. ExternalPath={ExternalPath}",
                settings.ExternalChdmanPath);
        }

        return _runtimeTools.GetChdmanPath();
    }

    public static bool TryResolveExternal(string? path, [NotNullWhen(true)] out string? resolvedPath)
    {
        resolvedPath = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            if (!File.Exists(fullPath))
            {
                return false;
            }

            if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));

            FileInfo fileInfo = new(fullPath);
            if (fileInfo.Length <= 0)
            {
                return false;
            }

            resolvedPath = fullPath;
            return true;
        }
        catch
        {
            return false;
        }
    }
}