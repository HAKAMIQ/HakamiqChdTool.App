using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
    public void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement element)
        {
            NormalizeToolTipOwner(element);
        }
    }

    private void NormalizeToolTipOwner(FrameworkElement element)
    {
        if (element.ToolTip is string text && !string.IsNullOrWhiteSpace(text))
        {
            element.ToolTip = CreateFluentToolTip(text);
        }
        else if (element.ToolTip is ToolTip toolTip)
        {
            ApplyFluentToolTipStyle(toolTip);
        }
    }

    private ToolTip CreateFluentToolTip(string text)
    {
        var toolTip = new ToolTip
        {
            Content = CreateToolTipTextBlock(text),
            FlowDirection = _owner.FlowDirection
        };

        ApplyFluentToolTipStyle(toolTip);
        return toolTip;
    }

    private TextBlock CreateToolTipTextBlock(string text)
    {
        var content = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FlowDirection = _owner.FlowDirection
        };

        content.SetResourceReference(TextElement.ForegroundProperty, "Brush.Text.Primary");
        content.SetResourceReference(TextElement.FontFamilyProperty, "FontFamily.Base");
        content.SetResourceReference(TextElement.FontSizeProperty, "FontSize.Caption");
        content.SetResourceReference(TextBlock.LineHeightProperty, "Font.LineHeight.ToolTip");

        return content;
    }

    private void ApplyFluentToolTipStyle(ToolTip toolTip)
    {
        toolTip.OverridesDefaultStyle = true;

        if (_owner.TryFindResource("OptionsToolTipStyle") is Style localStyle)
        {
            toolTip.Style = localStyle;
        }
        else if (_owner.TryFindResource("FluentToolTipStyle") is Style fluentStyle)
        {
            toolTip.Style = fluentStyle;
        }

        if (toolTip.Content is string text && !string.IsNullOrWhiteSpace(text))
        {
            toolTip.Content = CreateToolTipTextBlock(text);
        }

        toolTip.SetResourceReference(Control.BackgroundProperty, "Brush.Background.Elevated");
        toolTip.SetResourceReference(Control.ForegroundProperty, "Brush.Text.Primary");
        toolTip.SetResourceReference(Control.BorderBrushProperty, "Brush.Border.Subtle");
        toolTip.SetResourceReference(Control.FontFamilyProperty, "FontFamily.Base");
        toolTip.SetResourceReference(Control.FontSizeProperty, "FontSize.Caption");
        toolTip.SetResourceReference(Control.PaddingProperty, "Inset.12.All");
        toolTip.SetResourceReference(Control.BorderThicknessProperty, "Inset.1.All");
        toolTip.SetResourceReference(FrameworkElement.MaxWidthProperty, "Layout.ToolTip.MaxWidth");
    }
}