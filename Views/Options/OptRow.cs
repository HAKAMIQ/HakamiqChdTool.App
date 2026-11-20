using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace HakamiqChdTool.App.Views.Options;

/// <summary>Where an option row puts its control: at the start (check boxes, radio buttons) or at the end (lists).</summary>
public enum OptRowPlacement
{
    Trailing,
    Leading
}

/// <summary>
/// One option in the Options window: a title, a short description shown under it, the control, and an optional area
/// below the text (a folder box, a note). Every tab uses it, so rows have the same height, padding and text styles.
/// </summary>
public sealed class OptRow : ContentControl
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(
        nameof(Title), typeof(string), typeof(OptRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(
        nameof(Description), typeof(string), typeof(OptRow), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty PlacementProperty = DependencyProperty.Register(
        nameof(Placement), typeof(OptRowPlacement), typeof(OptRow), new PropertyMetadata(OptRowPlacement.Trailing));

    /// <summary>Set by the Options search when the row does not match; hides the row without touching its own Visibility.</summary>
    public static readonly DependencyProperty IsFilteredOutProperty = DependencyProperty.Register(
        nameof(IsFilteredOut), typeof(bool), typeof(OptRow), new PropertyMetadata(false));

    public static readonly DependencyProperty ExtraProperty = DependencyProperty.Register(
        nameof(Extra), typeof(object), typeof(OptRow), new PropertyMetadata(null));

    static OptRow()
    {
        FocusableProperty.OverrideMetadata(typeof(OptRow), new FrameworkPropertyMetadata(false));
    }

    public bool IsFilteredOut
    {
        get => (bool)GetValue(IsFilteredOutProperty);
        set => SetValue(IsFilteredOutProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public OptRowPlacement Placement
    {
        get => (OptRowPlacement)GetValue(PlacementProperty);
        set => SetValue(PlacementProperty, value);
    }

    /// <summary>Shown under the title and description, across the row (a folder box with its Browse button).</summary>
    public object? Extra
    {
        get => GetValue(ExtraProperty);
        set => SetValue(ExtraProperty, value);
    }

    // A click on the title or description of a check box or radio button row works like a click on the box itself.
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (e.Handled || Content is not ToggleButton toggle || !toggle.IsEnabled || !IsEnabled
            || toggle.IsMouseOver || (Extra is UIElement extra && extra.IsMouseOver))
        {
            return;
        }

        toggle.IsChecked = toggle is RadioButton || toggle.IsChecked != true;
        toggle.Focus();
        e.Handled = true;
    }
}

/// <summary>A group of option rows: a small heading above one card.</summary>
public sealed class OptSection : ItemsControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(OptSection), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsFilteredOutProperty = DependencyProperty.Register(
        nameof(IsFilteredOut), typeof(bool), typeof(OptSection), new PropertyMetadata(false));

    public bool IsFilteredOut
    {
        get => (bool)GetValue(IsFilteredOutProperty);
        set => SetValue(IsFilteredOutProperty, value);
    }

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }
}
