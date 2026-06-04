using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Views.Main;

public partial class ActionRailView : UserControl
{
    public ActionRailView()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? OperationModeChanged;

    public OperationModePanel OperationModePanel => ModePanel;

    public bool IsConvertMode => ModePanel.IsConvertMode;

    public bool IsExtractMode => ModePanel.IsExtractMode;

    public bool IsVerifyMode => ModePanel.IsVerifyMode;

    public string DropHintModeKey => ModePanel.DropHintModeKey;

    private void OnOperationModeChanged(object sender, RoutedEventArgs e)
    {
        OperationModeChanged?.Invoke(this, e);
    }
}
