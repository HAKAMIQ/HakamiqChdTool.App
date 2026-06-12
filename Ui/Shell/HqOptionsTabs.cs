using System;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
    public string ActiveTabKey => GetActiveTabKey();

    public void SelectTab(string? tabKey)
    {
        string normalizedTabKey = NormalizeTabKey(tabKey);

        _owner.GeneralTabButtonView.IsChecked = string.Equals(normalizedTabKey, GeneralTabKey, StringComparison.Ordinal);
        _owner.PathsTabButtonView.IsChecked = string.Equals(normalizedTabKey, PathsTabKey, StringComparison.Ordinal);
        _owner.RedumpTabButtonView.IsChecked = string.Equals(normalizedTabKey, RedumpTabKey, StringComparison.Ordinal);
        _owner.ProcessingTabButtonView.IsChecked = string.Equals(normalizedTabKey, ProcessingTabKey, StringComparison.Ordinal);
        _owner.PerformanceTabButtonView.IsChecked = string.Equals(normalizedTabKey, PerformanceTabKey, StringComparison.Ordinal);

        UpdateVisiblePanel();
    }

    private string GetActiveTabKey()
    {
        if (_owner.PathsTabButtonView?.IsChecked == true)
        {
            return PathsTabKey;
        }

        if (_owner.RedumpTabButtonView?.IsChecked == true)
        {
            return RedumpTabKey;
        }

        if (_owner.ProcessingTabButtonView?.IsChecked == true)
        {
            return ProcessingTabKey;
        }

        if (_owner.PerformanceTabButtonView?.IsChecked == true)
        {
            return PerformanceTabKey;
        }

        return GeneralTabKey;
    }

    private static string NormalizeTabKey(string? tabKey)
    {
        if (string.IsNullOrWhiteSpace(tabKey))
        {
            return GeneralTabKey;
        }

        string value = tabKey.Trim();

        if (value.Equals(PathsTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return PathsTabKey;
        }

        if (value.Equals(RedumpTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return RedumpTabKey;
        }

        if (value.Equals(ProcessingTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return ProcessingTabKey;
        }

        if (value.Equals(PerformanceTabKey, StringComparison.OrdinalIgnoreCase))
        {
            return PerformanceTabKey;
        }

        return GeneralTabKey;
    }

    public void OnTabButtonChecked(object sender, RoutedEventArgs e)
    {
        if (_owner.IsLoaded)
        {
            UpdateVisiblePanel();
        }
    }

    private void UpdateVisiblePanel()
    {
        if (_owner.GeneralPanelView is null
            || _owner.PathsPanelView is null
            || _owner.RedumpPanelView is null
            || _owner.ProcessingPanelView is null
            || _owner.PerformancePanelView is null)
        {
            return;
        }

        SetPanelVisibilityIfAvailable(_owner.GeneralPanelView, _owner.GeneralTabButtonView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(_owner.PathsPanelView, _owner.PathsTabButtonView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(_owner.RedumpPanelView, _owner.RedumpTabButtonView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(_owner.ProcessingPanelView, _owner.ProcessingTabButtonView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
        SetPanelVisibilityIfAvailable(_owner.PerformancePanelView, _owner.PerformanceTabButtonView.IsChecked == true ? Visibility.Visible : Visibility.Collapsed);
    }

    private static void SetPanelVisibilityIfAvailable(FrameworkElement? panel, Visibility visibility)
    {
        if (panel is null)
        {
            return;
        }

        panel.Visibility = visibility;
    }
}