using HakamiqChdTool.App.Localization;
using System;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    public double ProgressBarDisplayValue => BuildProgressBarDisplayValue(_queueProgressSmooth.DisplayValue);

    public double ProgressValue
    {
        get => _progressValue;
        set => SnapProgressTo(Math.Clamp(value, 0, 100));
    }

    public string ProgressText
    {
        get => _progressText;
        set
        {
            if (SetField(ref _progressText, value))
            {
                OnPropertyChanged(nameof(ProgressPercentDisplay));
            }
        }
    }

    public string ProgressPercentDisplay => FormatPercentStatic(ProgressBarDisplayValue);

    public long InputBytes
    {
        get => _inputBytes;
        set => SetField(ref _inputBytes, Math.Max(0, value));
    }

    public long OutputBytes
    {
        get => _outputBytes;
        set => SetField(ref _outputBytes, Math.Max(0, value));
    }

    public long CleanupDeletedBytes
    {
        get => _cleanupDeletedBytes;
        set => SetField(ref _cleanupDeletedBytes, Math.Max(0, value));
    }

    public int SbiCopiedCount
    {
        get => _sbiCopiedCount;
        set => SetField(ref _sbiCopiedCount, Math.Max(0, value));
    }

    public int PostProcessingFailureCount
    {
        get => _postProcessingFailureCount;
        set => SetField(ref _postProcessingFailureCount, Math.Max(0, value));
    }

    public double CompressionRatioPercent =>
        InputBytes <= 0 ? 0 : Math.Clamp((1.0 - (OutputBytes / (double)InputBytes)) * 100.0, -500, 100);

    public void AnimateProgressTo(double targetPercent)
    {
        _queueProgressSmooth.AnimateTo(Math.Clamp(targetPercent, 0, 100));
    }

    private void SnapProgressTo(double clamped)
    {
        _queueProgressSmooth.SnapTo(clamped);
    }

    private void OnQueueProgressSmoothDisplayChanged(object? sender, double value)
    {
        double clamped = Math.Clamp(value, 0, 100);
        double displayValue = BuildProgressBarDisplayValue(clamped);

        _progressValue = clamped;
        Pipeline.Progress = (int)Math.Round(displayValue);

        string nextText = FormatPercentStatic(displayValue);
        if (SetField(ref _progressText, nextText))
        {
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(ProgressPercentDisplay));
        }

        OnPropertyChanged(nameof(ProgressValue));
        OnPropertyChanged(nameof(ProgressBarDisplayValue));
    }

    private double BuildProgressBarDisplayValue(double value)
    {
        double clamped = Math.Clamp(value, 0, 100);
        if (clamped < 100)
        {
            return clamped;
        }

        return IsTerminalSuccessDisplayState() ? 100 : 99;
    }

    private bool IsTerminalSuccessDisplayState()
    {
        return QueueRowDisplayState == TaskQueueStateCodes.Completed
            && QueueRowDisplayFinalResult is TaskFinalResultCodes.Healthy
                or TaskFinalResultCodes.Moved
                or TaskFinalResultCodes.Extracted;
    }

    private void ApplySubStatusProgressHint(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
        {
            IsIndeterminate = false;
            return;
        }

        if (IsTerminalSuccessDisplayState() || TaskQueueStateCodes.IsTerminal(CurrentState))
        {
            IsIndeterminate = false;
            return;
        }

        IsIndeterminate = IsProgressActive && ProgressBarDisplayValue <= 0;
    }
}