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

    public async ValueTask<MediaInputDescriptor> IntakeAsync(
        string path,
        CancellationToken cancellationToken)
    {
        MediaInputPipelineDecision decision = await DecideAsync(path, cancellationToken)
            .ConfigureAwait(false);

        return decision.Descriptor;
    }

    public async ValueTask<MediaInputPipelineDecision> DecideAsync(
        string path,
        CancellationToken cancellationToken)
    {
        MediaInputDescriptor descriptor = await _classifier.ClassifyAsync(path, cancellationToken)
            .ConfigureAwait(false);

        return Decide(descriptor);
    }

    public static MediaInputPipelineDecision Decide(MediaInputDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        if (!descriptor.Exists)
        {
            return Block(descriptor, MediaInputPipelineDecisionReasons.InputMissingOrInvalid);
        }

        if (descriptor.Kind == MediaInputKind.Folder)
        {
            return descriptor.IsDirectory
                ? new MediaInputPipelineDecision(
                    descriptor,
                    MediaInputPipelineAction.AcceptFolder,
                    QueueInputRole.Unsupported,
                    descriptor.FullPath,
                    MediaInputPipelineDecisionReasons.FolderEnumeration,
                    MessageKey: null)
                : Block(descriptor, MediaInputPipelineDecisionReasons.FolderKindWithoutDirectory);
        }

        if (descriptor.Kind == MediaInputKind.CHD
            && descriptor.ProbeStatus != MediaInputProbeStatus.HeaderEnvelopeValid)
        {
            return Block(descriptor, MediaInputPipelineDecisionReasons.HeaderEvidenceRejected);
        }

        if (descriptor.Kind == MediaInputKind.CSO
            && descriptor.ProbeStatus != MediaInputProbeStatus.MagicConfirmed)
        {
            return Block(descriptor, MediaInputPipelineDecisionReasons.HeaderEvidenceRejected);
        }

        QueueInputRole role = MediaInputRoles.ResolveQueueRole(descriptor);
        return role switch
        {
            QueueInputRole.ConvertibleDiscImage => new MediaInputPipelineDecision(
                descriptor,
                MediaInputPipelineAction.AcceptConvertibleDiscImage,
                role,
                descriptor.FullPath,
                MediaInputPipelineDecisionReasons.ConvertibleDiscImage,
                MessageKey: null),

            QueueInputRole.ArchiveContainer => new MediaInputPipelineDecision(
                descriptor,
                MediaInputPipelineAction.AcceptArchiveContainer,
                role,
                descriptor.FullPath,
                MediaInputPipelineDecisionReasons.ArchiveContainer,
                MessageKey: null),

            QueueInputRole.ChdImage => new MediaInputPipelineDecision(
                descriptor,
                MediaInputPipelineAction.AcceptChdImage,
                role,
                descriptor.FullPath,
                MediaInputPipelineDecisionReasons.ChdImage,
                MessageKey: null),

            QueueInputRole.BinCueRescueCandidate => new MediaInputPipelineDecision(
                descriptor,
                MediaInputPipelineAction.RequiresStandaloneBinPolicy,
                role,
                descriptor.FullPath,
                MediaInputPipelineDecisionReasons.StandaloneBinPolicyRequired,
                MessageKey: null),

            QueueInputRole.DependentTrackFile => Block(descriptor, MediaInputPipelineDecisionReasons.DependentTrackFile),

            _ => descriptor.Kind == MediaInputKind.PKG
                ? new MediaInputPipelineDecision(
                    descriptor,
                    MediaInputPipelineAction.DetectedOnly,
                    QueueInputRole.Unsupported,
                    descriptor.FullPath,
                    MediaInputPipelineDecisionReasons.DetectedOnlyPackage,
                    MessageKey: null)
                : Block(descriptor, MediaInputPipelineDecisionReasons.UnsupportedMediaInput)
        };
    }

    private static MediaInputPipelineDecision Block(MediaInputDescriptor descriptor, string reason) => new(
        descriptor,
        MediaInputPipelineAction.Block,
        QueueInputRole.Unsupported,
        descriptor.FullPath,
        reason,
        "LocIntake_UnknownOrUnsupported");
}
