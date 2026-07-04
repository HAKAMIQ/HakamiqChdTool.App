using Serilog;
using System;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App.Services;

internal sealed class CsoTempWorkspace : IDisposable
{
    private static readonly ILogger Log = global::Serilog.Log.ForContext<CsoTempWorkspace>();

    private int _disposed;

    private CsoTempWorkspace(string directoryPath, string preparedIsoPath)
    {
        DirectoryPath = directoryPath;
        PreparedIsoPath = preparedIsoPath;
    }

    public string DirectoryPath { get; }

    public string PreparedIsoPath { get; }

    public bool TemporaryIsoDeleted { get; private set; }

    public static CsoTempWorkspace Create()
    {
        string directory = AppPaths.CombineProcessTemp("CsoPrepare_" + Guid.NewGuid().ToString("N"));
        string preparedIso = Path.Combine(directory, "prepared.iso");

        directory = Path.GetFullPath(directory);
        preparedIso = Path.GetFullPath(preparedIso);

        if (!AppPaths.IsPathUnderProcessTempRoot(directory)
            || !AppPaths.IsPathUnderProcessTempRoot(preparedIso)
            || !IsSamePathOrChild(preparedIso, directory))
        {
            throw new InvalidOperationException("LocAppPaths_OutsideProcessTempRoot");
        }

        Directory.CreateDirectory(directory);
        return new CsoTempWorkspace(directory, preparedIso);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        Cleanup();
    }

    private void Cleanup()
    {
        try
        {
            bool isoAbsentBeforeDelete = !File.Exists(PreparedIsoPath);

            if (Directory.Exists(DirectoryPath) && AppPaths.IsPathUnderProcessTempRoot(DirectoryPath))
            {
                Directory.Delete(DirectoryPath, recursive: true);
                Log.Information("Deleted CSO preparation temp workspace. Path={Path}", DirectoryPath);
            }

            TemporaryIsoDeleted = isoAbsentBeforeDelete || !File.Exists(PreparedIsoPath);
        }
        catch (Exception ex) when (ex is IOException
                                  or UnauthorizedAccessException
                                  or ArgumentException
                                  or NotSupportedException
                                  or PathTooLongException
                                  or System.Security.SecurityException)
        {
            TemporaryIsoDeleted = !File.Exists(PreparedIsoPath);
            Log.Debug(ex, "Could not delete CSO preparation temp workspace. Path={Path}", DirectoryPath);
        }
    }

    private static bool IsSamePathOrChild(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string TrimDirectorySeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
