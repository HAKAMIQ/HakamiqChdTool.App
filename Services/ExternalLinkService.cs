using Serilog;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed class ExternalLinkService : IExternalLinkService
{
    private const string InvalidLinkMessageKey = "LocExternalLink_InvalidLink";
    private const string UnsupportedLinkTypeMessageKey = "LocExternalLink_UnsupportedLinkType";
    private const string OpenFailedMessageKey = "LocExternalLink_OpenFailed";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<ExternalLinkService>();

    public bool TryOpen(string url, out string errorMessageKey)
    {
        errorMessageKey = string.Empty;

        if (string.IsNullOrWhiteSpace(url))
        {
            errorMessageKey = InvalidLinkMessageKey;
            return false;
        }

        string normalizedUrl = url.Trim();
        if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out Uri? uri))
        {
            Logger.Warning("ExternalLinkService rejected an invalid absolute URI. Url={Url}", normalizedUrl);
            errorMessageKey = InvalidLinkMessageKey;
            return false;
        }

        if (!IsSupportedScheme(uri.Scheme))
        {
            Logger.Warning("ExternalLinkService rejected an unsupported URI scheme. Scheme={Scheme}, Url={Url}", uri.Scheme, uri.AbsoluteUri);
            errorMessageKey = UnsupportedLinkTypeMessageKey;
            return false;
        }

        try
        {
            string target = uri.AbsoluteUri;
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false,
                ErrorDialog = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add(target);

            Logger.Debug(
                "ExternalLinkService launching URI via explorer. FileName={FileName}, Target={Target}",
                startInfo.FileName,
                target);

            using Process? process = Process.Start(startInfo);

            if (process is not null)
            {
                return true;
            }

            errorMessageKey = OpenFailedMessageKey;
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException
                                   or InvalidOperationException
                                   or IOException
                                   or Win32Exception
                                   or NotSupportedException
                                   or UnauthorizedAccessException
                                   or System.Security.SecurityException)
        {
            Logger.Warning(ex, "ExternalLinkService failed to launch URI. Url={Url}", uri.AbsoluteUri);
            errorMessageKey = OpenFailedMessageKey;
            return false;
        }
    }

    private static bool IsSupportedScheme(string scheme) =>
        string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
        || string.Equals(scheme, Uri.UriSchemeMailto, StringComparison.OrdinalIgnoreCase);
}
