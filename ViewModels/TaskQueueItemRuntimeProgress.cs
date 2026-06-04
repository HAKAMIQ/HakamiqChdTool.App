using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    private const char LeftToRightMark = '\u200E';
    private const string ChdmanOperationRuntimeDetailKey = "LocQueue_RuntimeProgress_ChdmanOperationDetail";

    private QueueRuntimeProgressKind _runtimeProgressKind = QueueRuntimeProgressKind.None;
    private string _runtimeProgressPrimaryMessageKey = string.Empty;
    private long _runtimeProgressCurrentBytes;
    private long _runtimeProgressTotalBytes;
    private double _runtimeProgressBytesPerSecond;
    private double _runtimeProgressPercent;
    private long _runtimeProgressElapsedTicks;
    private long _runtimeProgressEstimatedRemainingTicks;
    private string _runtimeProgressNextStageMessageKey = string.Empty;
    private bool _runtimeProgressShowActivitySpinner;

    public QueueRuntimeProgressKind RuntimeProgressKind
    {
        get => _runtimeProgressKind;
        private set
        {
            if (SetField(ref _runtimeProgressKind, value))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public string RuntimeProgressPrimaryMessageKey
    {
        get => _runtimeProgressPrimaryMessageKey;
        private set
        {
            if (SetField(ref _runtimeProgressPrimaryMessageKey, value))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public long RuntimeProgressCurrentBytes
    {
        get => _runtimeProgressCurrentBytes;
        private set
        {
            if (SetField(ref _runtimeProgressCurrentBytes, Math.Max(0, value)))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public long RuntimeProgressTotalBytes
    {
        get => _runtimeProgressTotalBytes;
        private set
        {
            if (SetField(ref _runtimeProgressTotalBytes, Math.Max(0, value)))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public double RuntimeProgressBytesPerSecond
    {
        get => _runtimeProgressBytesPerSecond;
        private set
        {
            double normalized = double.IsFinite(value) ? Math.Max(0d, value) : 0d;
            if (SetField(ref _runtimeProgressBytesPerSecond, normalized))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public double RuntimeProgressPercent
    {
        get => _runtimeProgressPercent;
        private set
        {
            double normalized = double.IsFinite(value) ? Math.Clamp(value, 0d, 100d) : 0d;
            if (SetField(ref _runtimeProgressPercent, normalized))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public long RuntimeProgressElapsedTicks
    {
        get => _runtimeProgressElapsedTicks;
        private set
        {
            if (SetField(ref _runtimeProgressElapsedTicks, Math.Max(0, value)))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public long RuntimeProgressEstimatedRemainingTicks
    {
        get => _runtimeProgressEstimatedRemainingTicks;
        private set
        {
            if (SetField(ref _runtimeProgressEstimatedRemainingTicks, Math.Max(0, value)))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public string RuntimeProgressNextStageMessageKey
    {
        get => _runtimeProgressNextStageMessageKey;
        private set
        {
            if (SetField(ref _runtimeProgressNextStageMessageKey, value))
            {
                NotifyRuntimeProgressChanged();
            }
        }
    }

    public bool RuntimeProgressShowActivitySpinner
    {
        get => _runtimeProgressShowActivitySpinner;
        private set
        {
            if (SetField(ref _runtimeProgressShowActivitySpinner, value))
            {
                OnPropertyChanged(nameof(ShowRuntimeActivitySpinner));
            }
        }
    }

    public bool HasRuntimeProgressDetail => RuntimeProgressKind != QueueRuntimeProgressKind.None;

    public bool ShowRuntimeActivitySpinner =>
        TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

    public bool ShowProgressPercent => !IsIndeterminate;

    public string RuntimeProgressDetailArabic => BuildRuntimeProgressDetailArabic();

    private string BuildRuntimeProgressDetailArabic()
    {
        if (RuntimeProgressKind == QueueRuntimeProgressKind.None)
        {
            return string.Empty;
        }
        return string.Empty;
    }

    private static string FormatInlineTechnicalSize(long bytes)
    {
        string value = DiskSpacePreflightService.FormatBytes(Math.Max(0, bytes));
        return string.Concat(LeftToRightMark, value, LeftToRightMark);
    }

    private static string FormatInlineTechnicalProgressBytes(long currentBytes, long totalBytes)
    {
        if (totalBytes > 0 && currentBytes >= 0 && currentBytes <= totalBytes)
        {
            string current = DiskSpacePreflightService.FormatBytes(currentBytes);
            string total = DiskSpacePreflightService.FormatBytes(totalBytes);
            return string.Concat(LeftToRightMark, current, " / ", total, LeftToRightMark);
        }

        return FormatInlineTechnicalSize(currentBytes);
    }

    private static string FormatInlineTechnicalRate(double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond) || bytesPerSecond <= 0d)
        {
            return ArabicUi.Get("LocQueue_RuntimeProgress_RateCalculating");
        }

        long roundedBytes = (long)Math.Round(Math.Min(bytesPerSecond, long.MaxValue));
        return ArabicUi.Format("LocQueue_RuntimeProgress_RateFormat", FormatInlineTechnicalSize(roundedBytes));
    }

    private static string FormatEstimatedRemaining(TimeSpan remaining)
    {
        return remaining > TimeSpan.Zero
            ? FormatElapsed(remaining)
            : ArabicUi.Get("LocQueue_RuntimeProgress_EtaCalculating");
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1d
            ? string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");
    }

    private void NotifyRuntimeProgressChanged()
    {
        OnPropertyChanged(nameof(HasRuntimeProgressDetail));
        OnPropertyChanged(nameof(RuntimeProgressDetailArabic));
        OnPropertyChanged(nameof(ShowRuntimeActivitySpinner));
        OnPropertyChanged(nameof(ShowProgressPercent));
        OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
        OnPropertyChanged(nameof(QueueRowExtendedTooltip));
        OnPropertyChanged(nameof(QueueRowDisplayDetailIsVisible));
        OnPropertyChanged(nameof(ProgressRegionPhaseIsolated));
        OnPropertyChanged(nameof(QueueRowDisplayPhaseIsolated));
    }
}
