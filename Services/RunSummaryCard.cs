using HakamiqChdTool.App.Core.Session;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace HakamiqChdTool.App.Services;

public enum RunSummaryOutcome
{
    Success = 0,
    WithProblems = 1,
    Stopped = 2
}

/// <summary>The short result shown above the queue after a run.</summary>
public sealed record RunSummaryCard(RunSummaryOutcome Outcome, string Title, string Message);

/// <summary>
/// Builds the one-line run result: counts that are not zero (done is always shown), space saved when a
/// conversion saved any, and the run time. Text comes from <paramref name="text"/> so the builder works
/// without WPF resources; numbers, sizes and times are wrapped in LTR marks for Arabic.
/// </summary>
public static class RunSummaryCardBuilder
{
    private const char LeftToRightMark = '‎';

    public static RunSummaryCard Build(SessionRunMetrics metrics, bool wasCancelled, TimeSpan elapsed, Func<string, string> text)
    {
        ArgumentNullException.ThrowIfNull(metrics);
        ArgumentNullException.ThrowIfNull(text);

        RunSummaryOutcome outcome = wasCancelled
            ? RunSummaryOutcome.Stopped
            : metrics.Failed > 0 ? RunSummaryOutcome.WithProblems : RunSummaryOutcome.Success;

        string title = text(outcome switch
        {
            RunSummaryOutcome.Stopped => "LocRunSummary_TitleStopped",
            RunSummaryOutcome.WithProblems => "LocRunSummary_TitleWithProblems",
            _ => "LocRunSummary_TitleDone"
        });

        var parts = new List<string> { Fill(text("LocRunSummary_Done"), Number(metrics.Completed)) };
        AddCount(parts, text, "LocRunSummary_Failed", metrics.Failed);
        AddCount(parts, text, "LocRunSummary_Skipped", metrics.Skipped);
        AddCount(parts, text, "LocRunSummary_Cancelled", metrics.Cancelled);

        if (metrics.SavedBytes > 0)
        {
            parts.Add(Fill(text("LocRunSummary_Saved"), Ltr(FormatBytes(metrics.SavedBytes))));
        }

        if (elapsed > TimeSpan.Zero)
        {
            parts.Add(Fill(text("LocRunSummary_Time"), Ltr(FormatElapsed(elapsed))));
        }

        return new RunSummaryCard(outcome, title, string.Join(" " + text("LocRunSummary_Separator").Trim() + " ", parts));
    }

    public static string FormatBytes(long bytes)
    {
        const double mib = 1024d * 1024d;
        const double gib = mib * 1024d;
        return bytes >= gib
            ? string.Format(CultureInfo.InvariantCulture, "{0:0.00} GB", bytes / gib)
            : string.Format(CultureInfo.InvariantCulture, "{0:0.0} MB", bytes / mib);
    }

    public static string FormatElapsed(TimeSpan elapsed)
    {
        TimeSpan rounded = TimeSpan.FromSeconds(Math.Max(1, Math.Round(elapsed.TotalSeconds)));
        return rounded.TotalHours >= 1
            ? string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", (int)rounded.TotalHours, rounded.Minutes, rounded.Seconds)
            : string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}", rounded.Minutes, rounded.Seconds);
    }

    private static void AddCount(List<string> parts, Func<string, string> text, string key, int count)
    {
        if (count > 0)
        {
            parts.Add(Fill(text(key), Number(count)));
        }
    }

    private static string Number(int value) => Ltr(value.ToString(CultureInfo.InvariantCulture));

    private static string Ltr(string value) => LeftToRightMark + value + LeftToRightMark;

    private static string Fill(string format, string value) =>
        format.Contains("{0}", StringComparison.Ordinal) ? format.Replace("{0}", value, StringComparison.Ordinal) : format + " " + value;
}
