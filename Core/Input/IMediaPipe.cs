using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Input;

public interface IMediaInputPipeline
{
    ValueTask<MediaInputDescriptor> IntakeAsync(
        string path,
        CancellationToken cancellationToken);

    ValueTask<MediaInputPipelineDecision> DecideAsync(
        string path,
        CancellationToken cancellationToken);
}
