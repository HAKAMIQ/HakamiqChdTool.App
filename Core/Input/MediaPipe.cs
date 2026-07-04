using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.IO;

namespace HakamiqChdTool.App.Core.Input;

public sealed class MediaInputPipeline : IMediaInputPipeline
{
    private readonly IMediaInputClassifier _classifier;
    private readonly IInputResolver _inputResolver;

    public MediaInputPipeline(IMediaInputClassifier classifier, IInputResolver inputResolver)
    {
        _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
        _inputResolver = inputResolver ?? throw new ArgumentNullException(nameof(inputResolver));
    }

    public IEnumerable<MediaInputDescriptor> Resolve(
        IReadOnlyList<string> rawPaths,
        QueueIngestKind inputKind,
        SearchOption searchOption)
    {
        ArgumentNullException.ThrowIfNull(rawPaths);

        foreach (string rawPath in rawPaths)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                continue;
            }

            MediaInputDescriptor descriptor = _classifier.Classify(rawPath);
            if (inputKind == QueueIngestKind.FilesOnly)
            {
                yield return descriptor;
                continue;
            }

            if (descriptor.Kind == MediaInputKind.Folder && descriptor.Exists && descriptor.IsDirectory)
            {
                foreach (string resolvedPath in _inputResolver.Resolve(descriptor.FullPath, searchOption))
                {
                    yield return _classifier.Classify(resolvedPath);
                }

                continue;
            }

            yield return descriptor;
        }
    }
}
