using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Globalization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    private RedumpOperationState _redumpState = RedumpOperationState.Idle;
    private double _redumpProgressValue;
    private bool _redumpIsIndeterminate;
    private string _redumpStatusText = string.Empty;
    private long _redumpCurrentBytes;
    private long _redumpTotalBytes;
    private double _redumpBytesPerSecond;
    private long _redumpEtaTicks;

    public RedumpOperationState RedumpState
    {
        get => _redumpState;
        private set
        {
            if (SetField(ref _redumpState, value))
            {
                OnPropertyChanged(nameof(IsRedumpOperationActive));
            }
        }
    }

    public bool IsRedumpOperationActive =>
        RedumpState is
            RedumpOperationState.Queued or
            RedumpOperationState.Hashing or
            RedumpOperationState.Matching;

    public double RedumpProgressValue
    {
        get => _redumpProgressValue;
        private set
        {
            double normalized = double.IsFinite(value)
                ? Math.Clamp(value, 0d, 100d)
                : 0d;

            if (SetField(ref _redumpProgressValue, normalized))
            {
                OnPropertyChanged(nameof(RedumpProgressPercentDisplay));
            }
        }
    }

    public bool RedumpIsIndeterminate
    {
        get => _redumpIsIndeterminate;
        private set
        {
            if (SetField(ref _redumpIsIndeterminate, value))
            {
                OnPropertyChanged(nameof(ShowRedumpProgressPercent));
            }
        }
    }

    public string RedumpStatusText
    {
        get => _redumpStatusText;
        private set
        {
            if (SetField(ref _redumpStatusText, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(RedumpProgressStatusDisplay));
            }
        }
    }

    public long RedumpCurrentBytes
    {
        get => _redumpCurrentBytes;
        private set
        {
            if (SetField(ref _redumpCurrentBytes, Math.Max(0L, value)))
            {
                NotifyRedumpMetricsChanged();
            }
        }
    }

    public long RedumpTotalBytes
    {
        get => _redumpTotalBytes;
        private set
        {
            if (SetField(ref _redumpTotalBytes, Math.Max(0L, value)))
            {
                NotifyRedumpMetricsChanged();
            }
        }
    }

    public double RedumpBytesPerSecond
    {
        get => _redumpBytesPerSecond;
        private set
        {
            double normalized = double.IsFinite(value)
                ? Math.Max(0d, value)
                : 0d;

            if (SetField(ref _redumpBytesPerSecond, normalized))
            {
                NotifyRedumpMetricsChanged();
            }
        }
    }

    public long RedumpEtaTicks
    {
        get => _redumpEtaTicks;
        private set
        {
            if (SetField(ref _redumpEtaTicks, Math.Max(0L, value)))
            {
                NotifyRedumpMetricsChanged();
            }
        }
    }

    public string RedumpProgressPercentDisplay =>
        FormatPercentStatic(RedumpProgressValue);

    public bool ShowRedumpProgressPercent =>
        !RedumpIsIndeterminate;

    public string RedumpProgressStatusDisplay =>
        string.IsNullOrWhiteSpace(RedumpStatusText)
            ? IntegrityStatusMessage
            : RedumpStatusText;

    public bool HasRedumpByteProgress =>
        RedumpTotalBytes > 0L;

    public bool HasRedumpSpeed =>
        RedumpBytesPerSecond > 0d;

    public bool HasRedumpEta =>
        RedumpEtaTicks > 0L;

    public bool HasRedumpMetrics =>
        HasRedumpByteProgress || HasRedumpSpeed || HasRedumpEta;

    public string RedumpBytesDisplay =>
        HasRedumpByteProgress
            ? IsolateRedumpTechnicalText(
                string.Concat(
                    FormatRedumpBytes(RedumpCurrentBytes),
                    " / ",
                    FormatRedumpBytes(RedumpTotalBytes)))
            : string.Empty;

    public string RedumpSpeedDisplay =>
        HasRedumpSpeed
            ? IsolateRedumpTechnicalText(
                string.Concat(
                    FormatRedumpBytes(
                        (long)Math.Round(
                            Math.Min(RedumpBytesPerSecond, long.MaxValue))),
                    "/s"))
            : string.Empty;

    public string RedumpEtaDisplay =>
        HasRedumpEta
            ? IsolateRedumpTechnicalText(
                string.Concat(
                    "ETA ",
                    FormatRedumpEta(TimeSpan.FromTicks(RedumpEtaTicks))))
            : string.Empty;

    public void SetRedumpProgress(
        RedumpOperationState state,
        string status,
        double progress,
        bool isIndeterminate,
        long currentBytes,
        long totalBytes,
        double bytesPerSecond,
        TimeSpan? eta)
    {
        RedumpState = state;
        RedumpStatusText = status;
        RedumpProgressValue = progress;
        RedumpIsIndeterminate = isIndeterminate;
        RedumpCurrentBytes = currentBytes;
        RedumpTotalBytes = totalBytes;
        RedumpBytesPerSecond = bytesPerSecond;
        RedumpEtaTicks = Math.Max(0L, eta?.Ticks ?? 0L);
    }

    public void ResetRedumpProgress()
    {
        SetRedumpProgress(
            RedumpOperationState.Idle,
            string.Empty,
            0d,
            isIndeterminate: false,
            currentBytes: 0L,
            totalBytes: 0L,
            bytesPerSecond: 0d,
            eta: null);
    }

    public string OperationLogDisplay =>
        QueueVerificationResultPresenter.BuildOperationLogDisplay(HasLogPath, LogPathDisplay);

    public bool HasOperationReport => HasLogPath;

    public bool IsVerificationReport =>
        QueueVerificationResultPresenter.IsVerificationReport(
            RequestedAction,
            FinalResult,
            IntegrityState);

    public string OperationReportTitle =>
        QueueVerificationResultPresenter.BuildOperationReportTitle(IsVerificationReport);

    public string OperationReportMessage =>
        QueueVerificationResultPresenter.BuildOperationReportMessage(
            IsVerificationReport,
            IntegrityStatusMessage,
            QueueRowDisplayDetailArabic,
            OperationLogDisplay);

    public bool HasVerificationResult =>
        QueueVerificationResultPresenter.HasVerificationResult(IsVerificationReport, LogPath);

    public string VerificationResultBadgeText =>
        QueueVerificationResultPresenter.BuildVerificationResultBadgeText(
            IntegrityState,
            FinalResult,
            IsVerificationReport,
            LogPath);

    private void NotifyRedumpMetricsChanged()
    {
        OnPropertyChanged(nameof(HasRedumpByteProgress));
        OnPropertyChanged(nameof(HasRedumpSpeed));
        OnPropertyChanged(nameof(HasRedumpEta));
        OnPropertyChanged(nameof(HasRedumpMetrics));
        OnPropertyChanged(nameof(RedumpBytesDisplay));
        OnPropertyChanged(nameof(RedumpSpeedDisplay));
        OnPropertyChanged(nameof(RedumpEtaDisplay));
    }

    private static string IsolateRedumpTechnicalText(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : "\u2066" + value + "\u2069";
    }

    private static string FormatRedumpBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = Math.Max(0L, bytes);
        int unitIndex = 0;

        while (value >= 1024d && unitIndex < units.Length - 1)
        {
            value /= 1024d;
            unitIndex++;
        }

        return unitIndex == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{value:0} {units[unitIndex]}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{value:0.##} {units[unitIndex]}");
    }

    private static string FormatRedumpEta(TimeSpan eta)
    {
        TimeSpan normalized = eta < TimeSpan.Zero
            ? TimeSpan.Zero
            : eta;

        return normalized.TotalHours >= 1d
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)normalized.TotalHours:00}:{normalized.Minutes:00}:{normalized.Seconds:00}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{normalized.Minutes:00}:{normalized.Seconds:00}");
    }
}