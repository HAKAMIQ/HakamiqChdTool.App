using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Input;

public interface IMediaInputClassifier
{
    ValueTask<MediaInputDescriptor> ClassifyAsync(
        string path,
        CancellationToken cancellationToken);
}
