namespace HakamiqChdTool.App.Services;

internal static class DiagnosticLogPolicy
{
    public static bool IsExpectedCancellation(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (exception is OperationCanceledException or TaskCanceledException)
        {
            return true;
        }

        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Count > 0
                && aggregateException.InnerExceptions.All(IsExpectedCancellation);
        }

        return exception.InnerException is not null && IsExpectedCancellation(exception.InnerException);
    }
}
