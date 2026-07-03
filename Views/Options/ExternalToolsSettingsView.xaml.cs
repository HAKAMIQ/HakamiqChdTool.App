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

    private void RecheckButton_Click(object sender, RoutedEventArgs e)
    {
        RecheckRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OpenToolsFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenToolsFolderRequested?.Invoke(this, EventArgs.Empty);
    }
}
