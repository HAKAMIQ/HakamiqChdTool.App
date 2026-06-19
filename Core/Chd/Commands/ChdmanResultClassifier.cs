namespace HakamiqChdTool.App.Core.Chd.Commands;

public static class ChdmanResultClassifier
{
    public static ChdConversionStatus Classify(
        int exitCode,
        bool userCanceled,
        string combinedOutput)
    {
        if (userCanceled)
        {
            return ChdConversionStatus.UserCanceled;
        }

        if (exitCode == 0)
        {
            return ChdConversionStatus.Success;
        }

        if (combinedOutput.Contains("file already exists", StringComparison.OrdinalIgnoreCase))
        {
            return ChdConversionStatus.SkippedOutputExists;
        }

        return ChdConversionStatus.Failed;
    }
}
