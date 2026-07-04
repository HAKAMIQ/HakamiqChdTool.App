using HakamiqChdTool.App.Models;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace HakamiqChdTool.App.Converters;

public sealed class ProcessingStateToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        string key = value switch
        {
            ProcessingState.Idle => "Brush.Text.Secondary",
            ProcessingState.Queued => "Brush.Warning",
            ProcessingState.AwaitingOperation => "Brush.Warning",
            ProcessingState.Processing => "Brush.Accent",
            ProcessingState.Skipped => "Brush.Warning",
            ProcessingState.Completed => "Brush.Success",
            ProcessingState.Failed => "Brush.Error",
            string text => GetBrushKey(text),
            _ => "Brush.Text.Secondary"
        };

        return ResolveBrush(key);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }

    private static string GetBrushKey(string value)
    {
        return value.Trim() switch
        {
            "Idle" => "Brush.Text.Secondary",
            "Queued" => "Brush.Warning",
            "AwaitingOperation" => "Brush.Warning",
            "Processing" => "Brush.Accent",
            "Skipped" => "Brush.Warning",
            "Completed" => "Brush.Success",
            "Failed" => "Brush.Error",
            _ => "Brush.Text.Secondary"
        };
    }

    private static object ResolveBrush(string key)
    {
        return System.Windows.Application.Current?.TryFindResource(key) as Brush
            ?? System.Windows.Application.Current?.TryFindResource("Brush.Text.Secondary") as Brush
            ?? DependencyProperty.UnsetValue;
    }
}