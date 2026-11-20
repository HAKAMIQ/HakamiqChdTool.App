using System;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class OptionsViewModel
{
    private static bool IsSafeDownloadUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return Uri.TryCreate(url.Trim(), UriKind.Absolute, out Uri? uri)
               && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }
}