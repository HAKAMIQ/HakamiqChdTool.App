using System.IO;

namespace HakamiqChdTool.App.Services;

public static class QueueConversionRules
{
    public static bool IsDiscOrArchiveSupportedForChdConversion(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || (!File.Exists(path) && !Directory.Exists(path)))
        {
            return false;
        }

        QueueInputClassification classification = QueueInputClassifier.Classify(path);
        return classification.IsConvertibleDiscImage || classification.IsArchiveContainer;
    }
}