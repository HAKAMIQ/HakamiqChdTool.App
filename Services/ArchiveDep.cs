using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

internal sealed class ArchiveDescriptorDependencyValidationResult
{
    public bool IsValid { get; init; }

    public string MessageResourceKey { get; init; } = string.Empty;

    public IReadOnlySet<string> RequiredKeys { get; init; } = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

    public static ArchiveDescriptorDependencyValidationResult Success(IReadOnlySet<string> requiredKeys)
    {
        System.ArgumentNullException.ThrowIfNull(requiredKeys);

        return new ArchiveDescriptorDependencyValidationResult
        {
            IsValid = true,
            RequiredKeys = new HashSet<string>(requiredKeys, System.StringComparer.OrdinalIgnoreCase)
        };
    }

    public static ArchiveDescriptorDependencyValidationResult Failure(
        string messageResourceKey,
        IReadOnlySet<string>? requiredKeys = null)
    {
        System.ArgumentException.ThrowIfNullOrWhiteSpace(messageResourceKey);

        return new ArchiveDescriptorDependencyValidationResult
        {
            IsValid = false,
            MessageResourceKey = messageResourceKey,
            RequiredKeys = requiredKeys is null
                ? new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(requiredKeys, System.StringComparer.OrdinalIgnoreCase)
        };
    }
}
