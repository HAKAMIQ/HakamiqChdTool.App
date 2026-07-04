using HakamiqChdTool.App.Models;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services.M3u;

public interface IM3uPlaylistGenerator
{
    M3uGenerationResult Generate(IEnumerable<MultiDiscSet> sets, bool overwriteExisting);
}
