using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private string _externalToolsCsoKitStatusText = ArabicUi.Get("LocExternalTools_CsoKitStatusMissing");
    private string _externalToolsCsoKitVersion = ArabicUi.Get("LocValue_Unavailable");
    private string _externalToolsCsoKitPath = ArabicUi.Get("LocValue_Unavailable");
    private bool _externalToolsCsoKitShowSetupNote = true;

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

    public bool ExternalToolsCsoKitShowSetupNote
    {
        get => _externalToolsCsoKitShowSetupNote;
        private set => SetProperty(ref _externalToolsCsoKitShowSetupNote, value);
    }

    public void SetCsoKitExternalToolStatus(
        string statusText,
        string version,
        string path,
        bool showSetupNote)
    {
        ExternalToolsCsoKitStatusText = string.IsNullOrWhiteSpace(statusText)
            ? ArabicUi.Get("LocExternalTools_CsoKitStatusMissing")
            : statusText;

        ExternalToolsCsoKitVersion = string.IsNullOrWhiteSpace(version)
            ? ArabicUi.Get("LocValue_Unavailable")
            : version;

        ExternalToolsCsoKitPath = string.IsNullOrWhiteSpace(path)
            ? ArabicUi.Get("LocValue_Unavailable")
            : path;

        ExternalToolsCsoKitShowSetupNote = showSetupNote;
    }
}
