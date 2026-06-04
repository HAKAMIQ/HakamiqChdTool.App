using System;

namespace HakamiqChdTool.App.Models;

public sealed record InputSafetyPolicy(
    bool IncludeSubfolders,
    int MaxArtifacts = InputSafetyPolicy.DefaultMaxArtifacts,
    int MaxFilesToScan = InputSafetyPolicy.DefaultMaxFilesToScan,
    int MaxDirectoriesToScan = InputSafetyPolicy.DefaultMaxDirectoriesToScan)
{
    public const int DefaultMaxArtifacts = 200;

    public const int MaximumMaxArtifacts = 200;

    public const int DefaultMaxFilesToScan = 10000;

    public const int MaximumMaxFilesToScan = 50000;

    public const int DefaultMaxDirectoriesToScan = 1000;

    public const int MaximumMaxDirectoriesToScan = 10000;

    public int MaxArtifacts { get; init; } = Math.Clamp(MaxArtifacts, 1, MaximumMaxArtifacts);

    public int MaxFilesToScan { get; init; } = Math.Clamp(MaxFilesToScan, 1, MaximumMaxFilesToScan);

    public int MaxDirectoriesToScan { get; init; } = Math.Clamp(MaxDirectoriesToScan, 1, MaximumMaxDirectoriesToScan);

    public static InputSafetyPolicy Minimum(bool includeSubfolders) =>
        new(includeSubfolders);
}
