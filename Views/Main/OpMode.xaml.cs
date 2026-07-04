using System.Windows;
using System.Windows.Controls;

namespace HakamiqChdTool.App.Views.Main;

public partial class OperationModePanel : UserControl
{
    public OperationModePanel()
    {
        InitializeComponent();
    }

    public event RoutedEventHandler? OperationModeChanged;

    public RadioButton ConvertRadio => ModeConvertRadio;

    public RadioButton ExtractRadio => ModeExtractRadio;

    public RadioButton VerifyRadio => ModeVerifyRadio;

    public RadioButton ScanConvertRadio => ModeScanConvertRadio;

    public RadioButton ScanExtractRadio => ModeScanExtractRadio;

    public bool IsConvertMode => ModeConvertRadio.IsChecked == true || ModeScanConvertRadio.IsChecked == true;

    public bool IsExtractMode => ModeExtractRadio.IsChecked == true || ModeScanExtractRadio.IsChecked == true;

    public bool IsVerifyMode => ModeVerifyRadio.IsChecked == true;

    public string DropHintModeKey
    {
        get
        {
            if (ModeVerifyRadio.IsChecked == true)
            {
                return "Verify";
            }

            if (ModeScanExtractRadio.IsChecked == true)
            {
                return "ScanExtract";
            }

            if (ModeExtractRadio.IsChecked == true)
            {
                return "Extract";
            }

            if (ModeScanConvertRadio.IsChecked == true)
            {
                return "ScanConvert";
            }

            return "Convert";
        }
    }

    private void OnOperationModeRadioClick(object sender, RoutedEventArgs e)
    {
        OperationModeChanged?.Invoke(this, e);
    }

}
