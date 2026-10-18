using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private string _externalToolsCsoKitStatusText = ArabicUi.Get("LocExternalTools_CsoKitStatusMissing");
    private string _externalToolsCsoKitVersion = ArabicUi.Get("LocValue_Unavailable");
    private string _externalToolsCsoKitPath = ArabicUi.Get("LocValue_Unavailable");
    private bool _externalToolsCsoKitIsAvailable;
    private bool _externalToolsCsoKitShowSetupNote = true;
    private string _externalToolsIgirStatusText = ArabicUi.Get("LocExternalTools_IgirStatusMissing");
    private string _externalToolsIgirVersion = ArabicUi.Get("LocValue_Unavailable");
    private string _externalToolsIgirPath = ArabicUi.Get("LocValue_Unavailable");
    private bool _externalToolsIgirIsAvailable;

    public string ExternalToolsIgirStatusText { get => _externalToolsIgirStatusText; private set => SetProperty(ref _externalToolsIgirStatusText, value); }
    public string ExternalToolsIgirVersion { get => _externalToolsIgirVersion; private set => SetProperty(ref _externalToolsIgirVersion, value); }
    public string ExternalToolsIgirPath { get => _externalToolsIgirPath; private set => SetProperty(ref _externalToolsIgirPath, value); }
    public bool ExternalToolsIgirIsAvailable { get => _externalToolsIgirIsAvailable; private set => SetProperty(ref _externalToolsIgirIsAvailable, value); }

    public void SetIgirExternalToolStatus(string status, string version, string path, bool available)
    {
        ExternalToolsIgirStatusText = status;
        ExternalToolsIgirVersion = string.IsNullOrWhiteSpace(version) ? ArabicUi.Get("LocValue_Unavailable") : version;
        ExternalToolsIgirPath = string.IsNullOrWhiteSpace(path) ? ArabicUi.Get("LocValue_Unavailable") : path;
        ExternalToolsIgirIsAvailable = available;
    }

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

    public bool ExternalToolsCsoKitIsAvailable
    {
        get => _externalToolsCsoKitIsAvailable;
        private set => SetProperty(ref _externalToolsCsoKitIsAvailable, value);
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
        bool isAvailable,
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

        ExternalToolsCsoKitIsAvailable = isAvailable;
        ExternalToolsCsoKitShowSetupNote = showSetupNote;
    }
}
