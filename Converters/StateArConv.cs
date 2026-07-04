using HakamiqChdTool.App.Localization;
using System;
using System.Globalization;
using System.Windows.Data;

namespace HakamiqChdTool.App.Converters;

public sealed class StateToArabicConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string? state = value?.ToString();

        return state switch
        {
            TaskQueueStateCodes.Pending => ArabicUi.Get("LocState_Pending"),
            TaskQueueStateCodes.Ready => ArabicUi.Get("LocState_Ready"),
            TaskQueueStateCodes.Extracting => ArabicUi.Get("LocState_Extracting"),
            TaskQueueStateCodes.PasswordRequired => ArabicUi.Get("LocState_PasswordRequired"),
            TaskQueueStateCodes.Failed => ArabicUi.Get("LocState_Failed"),
            TaskQueueStateCodes.Skipped => ArabicUi.Get("LocState_Skipped"),
            TaskQueueStateCodes.Cancelled => ArabicUi.Get("LocState_Cancelled"),
            TaskQueueStateCodes.Converting => ArabicUi.Get("LocState_Converting"),
            TaskQueueStateCodes.Verifying => ArabicUi.Get("LocState_Verifying"),
            TaskQueueStateCodes.Completed => ArabicUi.Get("LocState_Completed"),
            TaskQueueStateCodes.ReadingFile => ArabicUi.Get("LocState_ReadingFile"),
            TaskQueueStateCodes.Processing => ArabicUi.Get("LocState_Processing"),
            TaskQueueStateCodes.AwaitingOperationSelection => ArabicUi.Get("LocState_AwaitingOperationSelection"),
            _ => ArabicUi.Get("LocState_Unknown")
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}