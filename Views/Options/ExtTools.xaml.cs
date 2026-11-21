using System;
using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Views.Options;

public partial class ExternalToolsSettingsView : UserControl
{
    public ExternalToolsSettingsView()
    {
        InitializeComponent();
    }

    public event EventHandler? RecheckRequested;

    public event EventHandler? OpenToolsFolderRequested;

    public event EventHandler? BrowseCsoKitToolRequested;
    public event EventHandler? BrowseIgirToolRequested;

    public event EventHandler? UseBundledCsoKitRequested;

    public event EventHandler? UseBundledIgirRequested;

    private void UseBundledCsoKitButton_Click(object sender, RoutedEventArgs e) => UseBundledCsoKitRequested?.Invoke(this, EventArgs.Empty);

    private void UseBundledIgirButton_Click(object sender, RoutedEventArgs e) => UseBundledIgirRequested?.Invoke(this, EventArgs.Empty);

    private void RecheckButton_Click(object sender, RoutedEventArgs e)
    {
        RecheckRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenToolsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenToolsFolderRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BrowseCsoKitToolButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseCsoKitToolRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BrowseIgirToolButton_Click(object sender, RoutedEventArgs e) => BrowseIgirToolRequested?.Invoke(this, EventArgs.Empty);
}
