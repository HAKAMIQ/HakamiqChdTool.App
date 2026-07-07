using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private static string ResolveDialogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ArabicUi.ResolveDisplayString(value.Trim());
    }
}