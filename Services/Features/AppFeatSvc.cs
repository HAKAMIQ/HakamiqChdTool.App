using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Services.Features;


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
