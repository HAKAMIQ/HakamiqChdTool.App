using System.Collections.ObjectModel;

namespace HakamiqChdTool.App.Services;

internal sealed class DiskSpaceEstimate
{
    private readonly List<DiskSpaceRequirement> _requirements = new();

    public IReadOnlyList<DiskSpaceRequirement> Requirements =>
        new ReadOnlyCollection<DiskSpaceRequirement>(_requirements);

    public void AddRequirement(DiskSpaceRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        if (requirement.RequiredBytes <= 0)
        {
            return;
        }

        _requirements.Add(requirement);
    }

    public IReadOnlyDictionary<string, long> GetAggregatedRequirementsByDrive()
    {
        return _requirements
            .GroupBy(static requirement => NormalizeDriveRoot(requirement.DriveRoot), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                static group => group.Key,
                static group => group.Aggregate(0L, static (total, requirement) => SaturatingAdd(total, requirement.RequiredBytes)),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeDriveRoot(string driveRoot) =>
        string.IsNullOrWhiteSpace(driveRoot)
            ? string.Empty
            : driveRoot.Trim().ToUpperInvariant();

    private static long SaturatingAdd(long left, long right)
    {
        if (right > 0 && left > long.MaxValue - right)
        {
            return long.MaxValue;
        }

        return left + right;
    }
}
