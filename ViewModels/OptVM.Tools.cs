using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services.ExternalTools;
using System.Collections.Generic;
using System.Collections.ObjectModel;

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
    private bool _searchHasNoResults;

    /// <summary>True while the Options search text matches no option.</summary>
    public bool SearchHasNoResults
    {
        get => _searchHasNoResults;
        set => SetProperty(ref _searchHasNoResults, value);
    }

    /// <summary>The bundled tools (chdman, 7-Zip, Xbox tools) with their version and whether each file matches its pin.</summary>
    public ObservableCollection<BundledToolRow> BundledTools { get; } = [];

    public void SetBundledTools(IEnumerable<BundledToolStatus> tools)
    {
        BundledTools.Clear();
        foreach (BundledToolStatus tool in tools)
        {
            BundledTools.Add(new BundledToolRow(
                XboxViewModel.Ltr(tool.Name + " " + tool.Version),
                ArabicUi.Get(tool.PurposeKey),
                ArabicUi.Get(tool.State switch
                {
                    BundledToolState.Verified => "LocTools_StateVerified",
                    BundledToolState.Missing => "LocTools_StateMissing",
                    _ => "LocTools_StateChanged"
                }),
                tool.State == BundledToolState.Verified));
        }
    }
}

/// <summary>A row of the bundled tools list; the title is LTR text (name and version).</summary>
public sealed record BundledToolRow(string Title, string Purpose, string StateText, bool IsVerified);