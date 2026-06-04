using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Services.Licensing;

/// <summary>
/// License-free feature access policy for the public build.
/// All application features are available without a license file.
/// </summary>
public sealed class FeatureAccessService(ILicenseService licenseService) : IFeatureAccessService
{
    private static readonly PremiumFeature[] EnabledFeatures = Enum.GetValues<PremiumFeature>();

    private static readonly LicenseValidationResult LicenseFreeAccess = new(
        LicenseStatus.Active,
        "license-free",
        null,
        EnabledFeatures);

    private readonly ILicenseService _licenseService =
        licenseService ?? throw new ArgumentNullException(nameof(licenseService));

    public int FreeBatchLimit => int.MaxValue;

    public LicenseValidationResult CurrentLicense => LicenseFreeAccess;

    public bool IsPremiumActive => true;

    public bool CanUseFeature(PremiumFeature feature) =>
        Enum.IsDefined(feature);

    public string GetFeatureNameKey(PremiumFeature feature) => feature switch
    {
        PremiumFeature.UnlimitedBatch => "LocLicensing_FeatureUnlimitedBatch",
        PremiumFeature.AdvancedQueue => "LocLicensing_FeatureAdvancedQueue",
        PremiumFeature.PerformanceProfiles => "LocLicensing_FeaturePerformanceProfiles",
        PremiumFeature.RedumpDeepIntegrity => "LocLicensing_FeatureRedumpDeepIntegrity",
        PremiumFeature.RedumpDatabaseImport => "LocLicensing_FeatureRedumpDatabaseImport",
        PremiumFeature.StandardNamingSuggestion => "LocLicensing_FeatureStandardNamingSuggestion",
        PremiumFeature.PostProcessingAutomation => "LocLicensing_FeaturePostProcessingAutomation",
        PremiumFeature.StorageAdvisor => "LocLicensing_FeatureStorageAdvisor",
        _ => "LocLicensing_FeatureUnlimitedBatch"
    };

    public AppSettings CreateEffectiveSettings(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return settings;
    }

    public bool ApplyFreeFeatureRestrictions(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return false;
    }
}
