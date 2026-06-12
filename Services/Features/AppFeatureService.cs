using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Services.Features;

/// <summary>
/// Public feature policy for the unified build.
/// All product features are available to every user without a separate enablement step.
/// </summary>
public sealed class AppFeatureService : IAppFeatureService
{
    public bool IsEnabled(AppFeature feature) =>
        Enum.IsDefined(feature);

    public AppSettings CreateEffectiveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings;
    }

    public bool ApplyFeatureAvailability(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return false;
    }
}
