using HakamiqChdTool.App.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HakamiqChdTool.App.Services.Licensing;

public sealed class LicenseValidationResult
{
    private static readonly ReadOnlyCollection<PremiumFeature> NoFeatures =
        Array.AsReadOnly(Array.Empty<PremiumFeature>());

    private readonly HashSet<PremiumFeature> _featureSet;

    public LicenseValidationResult(
        LicenseStatus status,
        string licenseId,
        DateTimeOffset? expiresAtUtc,
        IEnumerable<PremiumFeature>? features)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown license status.");
        }

        Status = status;
        LicenseId = string.IsNullOrWhiteSpace(licenseId) ? string.Empty : licenseId.Trim();
        ExpiresAtUtc = expiresAtUtc;

        PremiumFeature[] normalizedFeatures = features?
            .Where(static feature => Enum.IsDefined(feature))
            .Distinct()
            .OrderBy(static feature => feature.ToString(), StringComparer.Ordinal)
            .ToArray() ?? [];

        Features = normalizedFeatures.Length == 0
            ? NoFeatures
            : Array.AsReadOnly(normalizedFeatures);

        _featureSet = [.. Features];
    }

    public LicenseStatus Status { get; }

    public string LicenseId { get; }

    public DateTimeOffset? ExpiresAtUtc { get; }

    public IReadOnlyList<PremiumFeature> Features { get; }

    public bool IsPremiumActive => Status == LicenseStatus.Active;

    public bool HasFeature(PremiumFeature feature) =>
        IsPremiumActive
        && Enum.IsDefined(feature)
        && _featureSet.Contains(feature);

    public static LicenseValidationResult Missing { get; } = new(
        LicenseStatus.Missing,
        string.Empty,
        null,
        null);

    public static LicenseValidationResult Failure(LicenseStatus status)
    {
        if (status == LicenseStatus.Active)
        {
            throw new ArgumentException("Failure result cannot use Active license status.", nameof(status));
        }

        return new LicenseValidationResult(
            status,
            string.Empty,
            null,
            null);
    }
}