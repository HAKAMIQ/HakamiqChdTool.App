using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services.Licensing;

public interface IFeatureAccessService
{
    int FreeBatchLimit { get; }

    LicenseValidationResult CurrentLicense { get; }

    bool IsPremiumActive { get; }

    bool CanUseFeature(PremiumFeature feature);

    string GetFeatureNameKey(PremiumFeature feature);

    AppSettings CreateEffectiveSettings(AppSettings settings);

    bool ApplyFreeFeatureRestrictions(AppSettings settings);
}
