using HakamiqChdTool.App.Models;
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

        if (!settings.UseBundledChdman && TryResolveExternal(settings.ExternalChdmanPath, out string? externalPath))
        {
            return externalPath;
        }

        return _runtimeTools.GetChdmanPath();
    }

    public bool IsExternalPathValid(string? path) => TryResolveExternal(path, out _);

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