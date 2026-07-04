namespace HakamiqChdTool.App.Services;

public sealed class SevenZipProcessResult
{
    public int ExitCode { get; init; }

    public bool WasCancelled { get; init; }

    public string StandardOutput { get; init; } = string.Empty;

    public string StandardError { get; init; } = string.Empty;

    public string CombinedOutput
    {
        get
        {
            if (string.IsNullOrWhiteSpace(StandardOutput))
            {
                return StandardError;
            }

            if (string.IsNullOrWhiteSpace(StandardError))
            {
                return StandardOutput;
            }

            return StandardOutput + Environment.NewLine + StandardError;
        }
    }
}