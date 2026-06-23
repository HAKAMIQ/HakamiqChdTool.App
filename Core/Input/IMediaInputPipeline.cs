using HakamiqChdTool.App.Models;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Core.Input;

public interface IMediaInputPipeline
{
    IEnumerable<MediaInputDescriptor> Resolve(
        IReadOnlyList<string> rawPaths,
        QueueIngestKind inputKind,
        SearchOption searchOption);
}
