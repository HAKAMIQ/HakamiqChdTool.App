using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
    private void RefreshAfterAppliedSettings(string previousLanguage)
    {
        if (_isClosed)
        {
            return;
        }

        AppLanguageService.ApplyToWindow(_owner);
        UpdateVisiblePanel();

        string currentLanguage = AppLanguageService.Instance.CurrentLanguageName;
        if (string.Equals(previousLanguage, currentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            InvalidateLayoutTree();
            return;
        }

        _ = _owner.Dispatcher.BeginInvoke(
            new Action(RefreshLayoutAfterLanguageSwitch),
            DispatcherPriority.Loaded);
    }

    private void RefreshLayoutAfterLanguageSwitch()
    {
        if (_isClosed || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished)
        {
            return;
        }

        AppLanguageService.ApplyToWindow(_owner);
        UpdateVisiblePanel();
        InvalidateLayoutTree();
        _owner.UpdateLayout();

        _ = _owner.Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (_isClosed || _owner.Dispatcher.HasShutdownStarted || _owner.Dispatcher.HasShutdownFinished)
                {
                    return;
                }

                UpdateVisiblePanel();
                InvalidateLayoutTree();
                _owner.UpdateLayout();
            }),
            DispatcherPriority.ContextIdle);
    }

    private void InvalidateLayoutTree()
    {
        foreach (FrameworkElement element in EnumerateFrameworkElements(_owner))
        {
            element.InvalidateMeasure();
            element.InvalidateArrange();
            element.InvalidateVisual();
        }
    }

    private static IEnumerable<FrameworkElement> EnumerateFrameworkElements(DependencyObject root)
    {
        if (root is FrameworkElement element)
        {
            yield return element;
        }

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < childCount; i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, i);
            foreach (FrameworkElement descendant in EnumerateFrameworkElements(child))
            {
                yield return descendant;
            }
        }
    }
}
