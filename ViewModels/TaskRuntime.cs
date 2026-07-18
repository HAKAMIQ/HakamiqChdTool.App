using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using System;
using System.Globalization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    private const char LeftToRightIsolate = '\u2066';
    private const char PopDirectionalIsolate = '\u2069';
    private const string TechnicalPlaceholder = "—";
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
                NotifyRuntimeProgressStateChanged();
            }
        }
    }

    public string RuntimeProgressPrimaryMessageKey
    {
        get => _runtimeProgressPrimaryMessageKey;
        private set => SetField(ref _runtimeProgressPrimaryMessageKey, value);
    }

    public long RuntimeProgressCurrentBytes
    {
        get => _runtimeProgressCurrentBytes;
        private set
        {
            if (SetField(ref _runtimeProgressCurrentBytes, Math.Max(0, value)))
            {
                NotifyRuntimeProgressMetricsChanged();
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
                NotifyRuntimeProgressMetricsChanged();
            }
        }
    }

    public double RuntimeProgressBytesPerSecond
    {
        get => _runtimeProgressBytesPerSecond;
        private set
        {
            double normalized = double.IsFinite(value)
                ? Math.Max(0d, value)
                : 0d;

            if (SetField(ref _runtimeProgressBytesPerSecond, normalized))
            {
                NotifyRuntimeProgressMetricsChanged();
            }
        }
    }

    public double RuntimeProgressPercent
    {
        get => _runtimeProgressPercent;
        private set
        {
            double normalized = double.IsFinite(value)
                ? Math.Clamp(value, 0d, 100d)
                : 0d;

            SetField(ref _runtimeProgressPercent, normalized);
        }
    }

    public long RuntimeProgressElapsedTicks
    {
        get => _runtimeProgressElapsedTicks;
        private set
        {
            if (SetField(ref _runtimeProgressElapsedTicks, Math.Max(0, value)))
            {
                NotifyRuntimeProgressMetricsChanged();
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
                NotifyRuntimeProgressMetricsChanged();
            }
        }
    }

    public string RuntimeProgressNextStageMessageKey
    {
        get => _runtimeProgressNextStageMessageKey;
        private set => SetField(ref _runtimeProgressNextStageMessageKey, value);
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

    public bool HasRuntimeProgressDetail =>
        RuntimeProgressKind != QueueRuntimeProgressKind.None
        || TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

    public bool ShowRuntimeActivitySpinner =>
        RuntimeProgressShowActivitySpinner
        && TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

    public bool ShowProgressPercent => !IsIndeterminate;

    public string RuntimeProgressDetailArabic =>
        BuildRuntimeProgressDetailArabic();

    private string BuildRuntimeProgressDetailArabic()
    {
        bool isActive =
            TaskQueueStateCodes.IsActiveRunning(QueueRowDisplayState);

        if (RuntimeProgressKind == QueueRuntimeProgressKind.None
            && !isActive)
        {
            return string.Empty;
        }

        string elapsed = FormatElapsed(
            new TimeSpan(RuntimeProgressElapsedTicks));

        string bytes = FormatInlineTechnicalProgressBytes(
            RuntimeProgressCurrentBytes,
            RuntimeProgressTotalBytes);

        string rate = FormatInlineTechnicalRate(
            RuntimeProgressBytesPerSecond);

        string remaining = FormatEstimatedRemaining(
            new TimeSpan(RuntimeProgressEstimatedRemainingTicks));

        return NormalizeWesternTechnicalText(
            ArabicUi.Format(
                ChdmanOperationRuntimeDetailKey,
                elapsed,
                bytes,
                rate,
                remaining));
    }

    private static string FormatInlineTechnicalSize(long bytes)
    {
        if (bytes <= 0)
        {
            return FormatTechnicalPlaceholder();
        }

        string value = DiskSpacePreflightService.FormatBytes(bytes);

        return IsolateTechnicalText(value);
    }

    private static string FormatInlineTechnicalProgressBytes(
        long currentBytes,
        long totalBytes)
    {
        long normalizedCurrent = Math.Max(0, currentBytes);
        long normalizedTotal = Math.Max(0, totalBytes);

        if (normalizedTotal > 0)
        {
            string current = normalizedCurrent > 0
                ? NormalizeWesternTechnicalText(
                    DiskSpacePreflightService.FormatBytes(
                        Math.Min(normalizedCurrent, normalizedTotal)))
                : TechnicalPlaceholder;

            string total = NormalizeWesternTechnicalText(
                DiskSpacePreflightService.FormatBytes(normalizedTotal));

            return IsolateTechnicalText(
                string.Concat(current, " / ", total));
        }

        return normalizedCurrent > 0
            ? FormatInlineTechnicalSize(normalizedCurrent)
            : FormatTechnicalPlaceholder();
    }

    private static string FormatInlineTechnicalRate(
        double bytesPerSecond)
    {
        if (!double.IsFinite(bytesPerSecond)
            || bytesPerSecond <= 0d)
        {
            return FormatTechnicalPlaceholder();
        }

        long roundedBytes = (long)Math.Round(
            Math.Min(bytesPerSecond, long.MaxValue));

        return NormalizeWesternTechnicalText(
            ArabicUi.Format(
                "LocQueue_RuntimeProgress_RateFormat",
                FormatInlineTechnicalSize(roundedBytes)));
    }

    private static string FormatEstimatedRemaining(
        TimeSpan remaining)
    {
        return remaining > TimeSpan.Zero
            ? FormatElapsed(remaining)
            : FormatTechnicalPlaceholder();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        string value = elapsed.TotalHours >= 1d
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"{(int)elapsed.TotalHours:00}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"{elapsed.Minutes:00}:{elapsed.Seconds:00}");

        return IsolateTechnicalText(value);
    }

    private static string FormatTechnicalPlaceholder() =>
        IsolateTechnicalText(TechnicalPlaceholder);

    private static string IsolateTechnicalText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return string.Concat(
            LeftToRightIsolate,
            NormalizeWesternTechnicalText(value),
            PopDirectionalIsolate);
    }

    private static string NormalizeWesternTechnicalText(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        char[] characters = value.ToCharArray();

        for (int index = 0; index < characters.Length; index++)
        {
            char character = characters[index];

            if (character is >= '\u0660' and <= '\u0669')
            {
                characters[index] =
                    (char)('0' + character - '\u0660');

                continue;
            }

            if (character is >= '\u06F0' and <= '\u06F9')
            {
                characters[index] =
                    (char)('0' + character - '\u06F0');

                continue;
            }

            characters[index] = character switch
            {
                '\u066B' => '.',
                '\u066C' => ',',
                _ => character
            };
        }

        return new string(characters);
    }

    private void NotifyRuntimeProgressStateChanged()
    {
        OnPropertyChanged(nameof(HasRuntimeProgressDetail));
        OnPropertyChanged(nameof(RuntimeProgressDetailArabic));
        OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
        OnPropertyChanged(nameof(QueueRowExtendedTooltip));
        OnPropertyChanged(nameof(QueueRowDisplayDetailIsVisible));
    }

    private void NotifyRuntimeProgressMetricsChanged()
    {
        OnPropertyChanged(nameof(RuntimeProgressDetailArabic));
        OnPropertyChanged(nameof(QueueRowDisplayDetailArabic));
        OnPropertyChanged(nameof(QueueRowExtendedTooltip));
    }
}