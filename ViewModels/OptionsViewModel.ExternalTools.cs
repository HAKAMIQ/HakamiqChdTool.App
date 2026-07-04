using HakamiqChdTool.App.Localization;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private string _externalToolsCsoKitStatusText = ArabicUi.Get("LocExternalTools_CsoKitStatusMissing");
    private string _externalToolsCsoKitVersion = ArabicUi.Get("LocValue_Unavailable");
    private string _externalToolsCsoKitPath = ArabicUi.Get("LocValue_Unavailable");

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
        ExternalToolsCsoKitStatusText = string.IsNullOrWhiteSpace(statusText)
            ? ArabicUi.Get("LocExternalTools_CsoKitStatusMissing")
            : statusText;

        ExternalToolsCsoKitVersion = NormalizeExternalToolsVersion(version);

        ExternalToolsCsoKitPath = string.IsNullOrWhiteSpace(path)
            ? ArabicUi.Get("LocValue_Unavailable")
            : path;
    }

    private static string NormalizeExternalToolsVersion(string? version)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            return ArabicUi.Get("LocValue_Unavailable");
        }

        string text = version.Trim();

        string[] prefixes =
        [
            "Hakamiq.CsoKit ",
            "Hakamiq CsoKit ",
            "Hakamiq.CsoKit",
            "Hakamiq CsoKit"
        ];

        foreach (string prefix in prefixes)
        {
            if (text.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(prefix.Length).Trim();
                break;
            }
        }

        return string.IsNullOrWhiteSpace(text)
            ? ArabicUi.Get("LocValue_Unavailable")
            : text;
    }
}
