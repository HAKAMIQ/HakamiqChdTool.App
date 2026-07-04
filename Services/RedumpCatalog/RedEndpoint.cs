using System;

namespace HakamiqChdTool.App.Services.RedumpCatalog;

internal sealed record RedumpArtifactEndpoint
{
    public RedumpArtifactEndpoint(
        RedumpArtifactKind kind,
        Uri primaryUri,
        Uri? mirrorUri = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        ArgumentNullException.ThrowIfNull(primaryUri);

        if (!IsSafeEndpointUri(primaryUri))
        {
            throw new ArgumentException(null, nameof(primaryUri));
        }

        if (mirrorUri is not null && !IsSafeEndpointUri(mirrorUri))
        {
            throw new ArgumentException(null, nameof(mirrorUri));
        }

        Kind = kind;
        PrimaryUri = primaryUri;
        MirrorUri = mirrorUri;
    }

    public RedumpArtifactKind Kind { get; }

    public Uri PrimaryUri { get; }

    public Uri? MirrorUri { get; }

    private static bool IsSafeEndpointUri(Uri uri)
    {
        return uri.IsAbsoluteUri
               && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
               && !string.IsNullOrWhiteSpace(uri.Host)
               && string.IsNullOrEmpty(uri.UserInfo);
    }
}