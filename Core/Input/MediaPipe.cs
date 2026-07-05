using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Input;

public sealed class MediaInputPipeline : IMediaInputPipeline
{
    private readonly IMediaInputClassifier _classifier;

    public MediaInputPipeline(IMediaInputClassifier classifier)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    }

    public ValueTask<MediaInputDescriptor> IntakeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        return _classifier.ClassifyAsync(path, cancellationToken);
    }
}
