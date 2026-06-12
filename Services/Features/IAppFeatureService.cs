using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services.Features;

public interface IAppFeatureService
{
    bool IsEnabled(AppFeature feature);

    AppSettings CreateEffectiveSettings(AppSettings settings);

    bool ApplyFeatureAvailability(AppSettings settings);
}
