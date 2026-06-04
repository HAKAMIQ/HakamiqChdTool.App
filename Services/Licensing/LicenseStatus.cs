namespace HakamiqChdTool.App.Services.Licensing;

public enum LicenseStatus
{
    Missing,
    Active,
    InvalidFormat,
    InvalidSignature,
    NotYetValid,
    Expired,
    MachineMismatch,
    FeatureNotLicensed,
    Error
}
