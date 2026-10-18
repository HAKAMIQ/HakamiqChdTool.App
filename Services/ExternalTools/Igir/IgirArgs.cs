using HakamiqChdTool.App.Core.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Services.ExternalTools.Igir;

public sealed record IgirReportRequest(string LibraryRoot, IReadOnlyList<string> DatPaths, string WorkRoot);

public static class IgirArgs
{
    public static IReadOnlyList<string> BuildReport(IgirReportRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        string library = Path.GetFullPath(request.LibraryRoot);
        string workRoot = Path.GetFullPath(request.WorkRoot);
        if (!Directory.Exists(library) || !InputResolver.IsSafeExistingPath(library))
            throw new IOException("igir input root is missing or unsafe.");
        if (IsSameOrInside(workRoot, library) || IsSameOrInside(library, workRoot))
            throw new IOException("igir work files must be outside the user library.");
        if (!Path.IsPathFullyQualified(workRoot)) throw new ArgumentException("igir work root must be absolute.", nameof(request));
        string report = Path.Combine(workRoot, "report.csv");
        string cacheRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HakamiqChdTool", "Igir");
        if (!IsSameOrInside(workRoot, cacheRoot) || workRoot.Equals(cacheRoot, StringComparison.OrdinalIgnoreCase))
            throw new IOException("igir reports and temporary files must stay in an app-owned run directory.");
        string cache = Path.Combine(cacheRoot, "igir.cache");
        string temp = Path.Combine(workRoot, "temp");
        List<string> args = ["report"];
        foreach (string dat in request.DatPaths.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string fullDat = Path.GetFullPath(dat);
            if (!File.Exists(fullDat) || !InputResolver.IsSafeExistingPath(fullDat))
                throw new IOException("igir DAT path is missing or unsafe.");
            args.Add("--dat"); args.Add(fullDat);
        }
        if (request.DatPaths.Count == 0) throw new ArgumentException("At least one DAT is required.", nameof(request));
        args.AddRange(["--input", library, "--report-output", report, "--cache-path", cache,
            "--temp-dir", temp, "--input-checksum-min", "SHA1", "--input-checksum-max", "SHA1",
            "--reader-threads", "2", "--dat-threads", "2"]);
        return args;
    }

    private static bool IsSameOrInside(string path, string root)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return fullPath.Equals(fullRoot, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
