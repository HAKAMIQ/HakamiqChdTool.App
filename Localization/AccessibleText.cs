using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;

namespace HakamiqChdTool.App.Localization;

// Localized strings keep invisible bidi isolates and marks for correct rendering, but
// screen readers must not receive them. A TextBlock's text and a string tooltip become
// accessible names and help texts by default, so those defaults are replaced with a
// cleaned copy. Explicit AutomationProperties values always win.
internal static class AccessibleText
{
    private static readonly IValueConverter Cleaner = new CleanTextConverter();
    private static bool _registered;

    internal static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;

        // Loaded is only broadcast into subtrees that registered instance handlers, so it
        // misses most text. Every element raises SizeChanged on its first layout; after the
        // binding is in place later events return after a cheap value-source check.
        EventManager.RegisterClassHandler(
            typeof(FrameworkElement),
            FrameworkElement.SizeChangedEvent,
            new SizeChangedEventHandler(OnElementSized),
            handledEventsToo: true);
    }

    internal static string Clean(string? value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : string.Concat(value.Where(ch => !IsBidiControl(ch)));
    }

    private static bool IsBidiControl(char ch)
    {
        return ch is '؜' or '‎' or '‏'
            or (>= '‪' and <= '‮')
            or (>= '⁦' and <= '⁩');
    }

    private static void OnElementSized(object sender, SizeChangedEventArgs e)
    {
        switch (sender)
        {
            case TextBlock textBlock:
                BindIfUnset(textBlock, AutomationProperties.NameProperty, nameof(TextBlock.Text));
                BindIfUnset(textBlock, AutomationProperties.HelpTextProperty, nameof(FrameworkElement.ToolTip));
                break;

            case ContentControl contentControl:
                // Buttons, radio buttons and check boxes are named from their content text.
                if (contentControl.Content is TextBlock)
                {
                    BindIfUnset(contentControl, AutomationProperties.NameProperty, "Content.Text");
                }
                else if (contentControl.Content is string)
                {
                    BindIfUnset(contentControl, AutomationProperties.NameProperty, nameof(ContentControl.Content));
                }

                BindIfUnset(contentControl, AutomationProperties.HelpTextProperty, nameof(FrameworkElement.ToolTip));
                break;

            case Control control:
                // Tooltips are often assigned after the first layout, so bind regardless.
                BindIfUnset(control, AutomationProperties.HelpTextProperty, nameof(FrameworkElement.ToolTip));
                break;
        }
    }

    private static void BindIfUnset(FrameworkElement element, DependencyProperty property, string sourcePath)
    {
        if (DependencyPropertyHelper.GetValueSource(element, property).BaseValueSource != BaseValueSource.Default)
        {
            return;
        }

        element.SetBinding(
            property,
            new Binding(sourcePath)
            {
                Source = element,
                Converter = Cleaner,
                Mode = BindingMode.OneWay
            });
    }

    private sealed class CleanTextConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            string? text = value switch
            {
                string s => s,
                ToolTip { Content: string s } => s,
                ToolTip { Content: TextBlock block } => block.Text,
                _ => null
            };

            // Leave the default accessible text in place unless it carries bidi controls.
            return text is not null && text.Any(IsBidiControl)
                ? Clean(text)
                : DependencyProperty.UnsetValue;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}
