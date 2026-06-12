using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Views.Options;

public partial class RedumpSettingsView : UserControl
{
    public RedumpSettingsView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? DownloadDatabaseRequested;

    public event RoutedEventHandler? ImportRedumpDatabaseRequested;

    public TextBlock DatabaseLastSyncedTextBlockView => DatabaseLastSyncedTextBlock;

    public TextBlock DatabaseStatusTextBlockView => DatabaseStatusTextBlock;

    public TextBox DatabaseDownloadUrlTextBoxView => DatabaseDownloadUrlTextBox;

    public Button DownloadDatabaseButtonView => DownloadDatabaseButton;

    public ProgressBar DatabaseDownloadProgressBarView => DatabaseDownloadProgressBar;

    public TextBlock DatabaseDownloadStatusTextBlockView => DatabaseDownloadStatusTextBlock;

    public TextBox RedumpSystemNameTextBoxView => RedumpSystemNameTextBox;

    public Button BrowseRedumpDatButtonView => BrowseRedumpDatButton;

    public TextBox RedumpDatPathTextBoxView => RedumpDatPathTextBox;

    public Button ImportRedumpDatabaseButtonView => ImportRedumpDatabaseButton;

    public ProgressBar RedumpImportProgressBarView => RedumpImportProgressBar;

    public TextBlock RedumpImportStatusTextBlockView => RedumpImportStatusTextBlock;

    public Border IntegrityFeatureCardView => IntegrityFeatureCard;

    public CheckBox EnableDeepIntegrityCheckCheckBoxView => EnableDeepIntegrityCheckCheckBox;

    public CheckBox ApplyStandardNamingBasedOnHashCheckBoxView => ApplyStandardNamingBasedOnHashCheckBox;

    private void DownloadDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        DownloadDatabaseRequested?.Invoke(sender, e);
    }

    private void ImportRedumpDatabaseButton_Click(object sender, RoutedEventArgs e)
    {
        ImportRedumpDatabaseRequested?.Invoke(sender, e);
    }
}
