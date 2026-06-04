using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Services.Licensing;

/// <summary>
/// License-free service retained only to preserve existing composition contracts.
/// This build does not read, validate, import, or require any license file.
/// </summary>
public sealed class LicenseService : ILicenseService
{
    private static readonly PremiumFeature[] EnabledFeatures = Enum.GetValues<PremiumFeature>();

    private static readonly LicenseValidationResult LicenseFreeAccess = new(
        LicenseStatus.Active,
        "license-free",
        null,
        EnabledFeatures);

    public LicenseValidationResult Current => LicenseFreeAccess;

    public bool IsPremiumActive => true;

    public LicenseValidationResult Refresh() => LicenseFreeAccess;

    public bool HasFeature(PremiumFeature feature) =>
        Enum.IsDefined(feature);
}
