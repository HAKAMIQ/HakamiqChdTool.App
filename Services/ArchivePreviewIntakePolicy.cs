using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public static class ArchivePreviewIntakePolicy
{
    public static bool AllowsArchivePreview(QueueIntakeSource source) => false;

    public static bool ShouldPreviewArchive(QueueInputClassification classification, QueueIntakeSource source) =>
        classification.IsArchiveContainer && AllowsArchivePreview(source);

    public static bool ShouldPreviewArchive(string? path, QueueIntakeSource source) =>
        ShouldPreviewArchive(QueueInputClassifier.Classify(path), source);

    public static bool AllowsQueuedArchiveProcessing(string? path, QueueIntakeSource source)
    {
        QueueInputClassification classification = QueueInputClassifier.Classify(path);
        return AllowsQueuedArchiveProcessing(classification, source);
    }

    public static bool AllowsQueuedArchiveProcessing(QueueInputClassification classification, QueueIntakeSource source)
    {
        _ = classification;
        _ = source;

        return true;
    }

    public static bool BlocksQueuedArchiveProcessing(string? path, QueueIntakeSource source) =>
        !AllowsQueuedArchiveProcessing(path, source);
}
