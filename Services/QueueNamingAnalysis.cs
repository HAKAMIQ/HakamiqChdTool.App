using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.ViewModels;
using Serilog;
using System.IO;

namespace HakamiqChdTool.App.Services;

public static class QueueNamingAnalysis
{
    private static readonly ILogger Logger = global::Serilog.Log.ForContext(typeof(QueueNamingAnalysis));

    public static void Apply(TaskQueueItemViewModel item, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.EnableDeepIntegrityCheck)
        {
            ClearSuggestion(item);
            return;
        }

        if (!HasRedumpDatabaseRows())
        {
            ClearSuggestion(item);
            return;
        }

        try
        {
            (bool compliant, string suggested) = NamingCorrectionEngine.Analyze(item.SourcePath);
            item.IsNamingCompliant = compliant;
            item.SuggestedStandardName = suggested;
        }
        catch (Exception ex) when (IsExpectedNamingAnalysisException(ex))
        {
            Logger.Debug(ex, "Queue naming analysis failed. SourcePath={SourcePath}", item.SourcePath);
            ClearSuggestion(item);
        }
    }

    private static bool HasRedumpDatabaseRows()
    {
        try
        {
            RedumpSqliteManager database = RedumpSqliteManager.Default;
            database.EnsureInitialized();
            return database.HasAnyRows();
        }
        catch (Exception ex) when (IsExpectedDatabaseException(ex))
        {
            Logger.Debug(ex, "Queue naming analysis skipped because Redump database is unavailable.");
            return false;
        }
    }

    private static void ClearSuggestion(TaskQueueItemViewModel item)
    {
        item.IsNamingCompliant = true;
        item.SuggestedStandardName = string.Empty;
    }

    private static bool IsExpectedNamingAnalysisException(Exception ex) =>
        ex is ArgumentException
        or NotSupportedException
        or PathTooLongException;

    private static bool IsExpectedDatabaseException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or InvalidOperationException
        or NotSupportedException
        or PathTooLongException
        || string.Equals(ex.GetType().FullName, "Microsoft.Data.Sqlite.SqliteException", StringComparison.Ordinal);
}