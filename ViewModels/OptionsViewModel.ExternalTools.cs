using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private string _externalToolsCsoKitStatusText = ArabicUi.Get("LocExternalTools_CsoKitStatusMissing");
    private string _externalToolsCsoKitVersion = string.Empty;
    private string _externalToolsCsoKitPath = string.Empty;

    public string ExternalToolsCsoKitStatusText
    {
        get => _externalToolsCsoKitStatusText;
        private set => SetProperty(ref _externalToolsCsoKitStatusText, value);
    }

    public string ExternalToolsCsoKitVersion
    {
        get => _externalToolsCsoKitVersion;
        private set => SetProperty(ref _externalToolsCsoKitVersion, value);
    }

    public string ExternalToolsCsoKitPath
    {
        get => _externalToolsCsoKitPath;
        private set => SetProperty(ref _externalToolsCsoKitPath, value);
    }

    public void SetCsoKitExternalToolStatus(
        string statusText,
        string version,
        string path)
    {
        ExternalToolsCsoKitStatusText = statusText ?? string.Empty;
        ExternalToolsCsoKitVersion = version ?? string.Empty;
        ExternalToolsCsoKitPath = path ?? string.Empty;
    }
}
