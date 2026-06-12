using System;

namespace HakamiqChdTool.App.Models.Chd;

public sealed record ChdmanCapabilitySnapshot(
    string ExecutablePath,
    string Version,
    bool IsAvailable,
    bool SupportsCreateDvd,
    bool SupportsExtractDvd,
    bool SupportsZstd,
    bool SupportsHunkSizeOption,
    bool HasReliableHelpText,
    string MessageKey,
    string DiagnosticSummary)
{
    public static ChdmanCapabilitySnapshot Unavailable(
        string executablePath,
        string messageKey,
        string diagnosticSummary = "") => new(
            executablePath,
            string.Empty,
            IsAvailable: false,
            SupportsCreateDvd: false,
            SupportsExtractDvd: false,
            SupportsZstd: false,
            SupportsHunkSizeOption: false,
            HasReliableHelpText: false,
            string.IsNullOrWhiteSpace(messageKey)
                ? "LocChdPolicy_CapabilityProbeFailed"
                : messageKey,
            diagnosticSummary);

    public bool SupportsRequestedCompression(string command, string? resolvedCompression)
    {
        if (!IsCreateCommand(command) || string.IsNullOrWhiteSpace(resolvedCompression))
        {
            return true;
        }

        string value = resolvedCompression.Trim();
        if (string.Equals(value, "default", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "none", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string[] codecs = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string codec in codecs)
        {
            if (string.Equals(codec, "zstd", StringComparison.OrdinalIgnoreCase) && !SupportsZstd)
            {
                return false;
            }
        }

        return true;
    }

    public bool SupportsRequestedHunkSize(string command, int hunkSizeBytes)
    {
        if (!IsCreateCommand(command) || hunkSizeBytes <= 0)
        {
            return true;
        }

        return !HasReliableHelpText || SupportsHunkSizeOption;
    }

    private static bool IsCreateCommand(string command) =>
        string.Equals(command, "createcd", StringComparison.OrdinalIgnoreCase)
        || string.Equals(command, "createdvd", StringComparison.OrdinalIgnoreCase);
}