using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Startup;

/// <summary>Decides how an Explorer menu launch is shown, and when the quick window must ask before it starts.</summary>
internal static class QuickLaunch
{
    /// <summary>
    /// The first instance shows the quick window for an Explorer launch (<c>--quick</c>) with a mode and paths, when the
    /// setting is on. Any other launch opens the main window as before.
    /// </summary>
    public static bool OpensQuickWindow(LaunchRequest request, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(settings);
        return request.Quick
            && request.HasPaths
            && request.Mode != LaunchMode.None
            && settings.ExplorerOpensQuickWindow;
    }

    /// <summary>
    /// True when the run may delete the source after it succeeds, so the quick window waits for Start instead of
    /// starting on its own. Conversion deletes only after a verified conversion; extraction only after a proven
    /// extraction (FlowCleanup); verification never deletes.
    /// </summary>
    public static bool MayDeleteSource(LaunchMode mode, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return mode switch
        {
            LaunchMode.Convert => settings.DeleteSourceAfterVerifiedConversion && settings.VerifyAfterConversion,
            LaunchMode.Extract => settings.DeleteSourceAfterVerifiedExtraction,
            _ => false
        };
    }
}
