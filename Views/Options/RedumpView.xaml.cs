using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Views.Options;

public partial class RedumpSettingsView : UserControl
{
    public RedumpSettingsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? ImportRedumpDatabaseRequested;

    public Button ImportRedumpDatabaseButtonView => ImportRedumpDatabaseButton;

    public ProgressBar RedumpImportProgressBarView => RedumpImportProgressBar;

    public TextBlock RedumpImportStatusTextBlockView => RedumpImportStatusTextBlock;

    private void ImportRedumpDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        ImportRedumpDatabaseRequested?.Invoke(sender, e);
    }
}
