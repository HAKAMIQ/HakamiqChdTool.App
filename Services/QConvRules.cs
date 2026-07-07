using HakamiqChdTool.App.Core.Input;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public static class QueueConversionRules
{
    public static bool IsDiscOrArchiveSupportedForChdConversion(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path.Trim());
            ConversionPathValidator.ThrowIfUnsafeForChdman(fullPath, nameof(path));
        }
        catch (Exception ex) when (IsExpectedPathException(ex))
        {
            return false;
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return false;
        }

        QueueInputClassification classification = QueueInputClassifier.Classify(fullPath);
        return classification.IsConvertibleDiscImage || classification.IsArchiveContainer;
    }

    private static bool IsExpectedPathException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException
        or IOException
        or UnauthorizedAccessException
        or System.Security.SecurityException;
}