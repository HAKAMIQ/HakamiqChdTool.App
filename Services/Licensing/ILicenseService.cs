using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services.Licensing;

public interface ILicenseService
{
    LicenseValidationResult Current { get; }

    LicenseValidationResult Refresh();

    bool IsPremiumActive { get; }

    bool HasFeature(PremiumFeature feature);
}
