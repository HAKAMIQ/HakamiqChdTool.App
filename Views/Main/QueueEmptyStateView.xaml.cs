using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Views.Main;

public partial class QueueEmptyStateView : UserControl
{
    public static readonly DependencyProperty OperationModeKeyProperty =
        DependencyProperty.Register(
            nameof(OperationModeKey),
            typeof(string),
            typeof(QueueEmptyStateView),
            new PropertyMetadata("Convert"));

    public QueueEmptyStateView()
    {
        InitializeComponent();
    }

    public string OperationModeKey
    {
        get => (string)GetValue(OperationModeKeyProperty);
        set => SetValue(OperationModeKeyProperty, string.IsNullOrWhiteSpace(value) ? "Convert" : value);
    }
}
