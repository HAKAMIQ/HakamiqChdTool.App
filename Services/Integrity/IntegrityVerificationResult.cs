using System;
using System.IO;

namespace HakamiqChdTool.App.Services.Integrity;

public sealed class IntegrityVerificationResult
{
    private IntegrityVerificationResult(
        IntegrityVerificationStatus status,
        string filePath,
        string? relativePath,
        string? expectedSha256,
        string? actualSha256)
    {
        if (!Enum.IsDefined(status))
        {
            throw new ArgumentOutOfRangeException(nameof(status), status, null);
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string? normalizedRelativePath = NormalizeRelativePath(relativePath);
        string? normalizedExpectedSha256 = NormalizeSha256(expectedSha256);
        string? normalizedActualSha256 = NormalizeSha256(actualSha256);

        if (status == IntegrityVerificationStatus.Passed)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizedRelativePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizedExpectedSha256);
            ArgumentException.ThrowIfNullOrWhiteSpace(normalizedActualSha256);

            if (!string.Equals(normalizedExpectedSha256, normalizedActualSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(null, nameof(actualSha256));
            }
        }

        Status = status;
        FilePath = Path.GetFullPath(filePath);
        RelativePath = normalizedRelativePath;
        ExpectedSha256 = normalizedExpectedSha256;
        ActualSha256 = normalizedActualSha256;
    }

    public IntegrityVerificationStatus Status { get; }

    public string FilePath { get; }

    public string? RelativePath { get; }

    public string? ExpectedSha256 { get; }

    public string? ActualSha256 { get; }

    public bool IsPassed => Status == IntegrityVerificationStatus.Passed;

    public static IntegrityVerificationResult Passed(
        string filePath,
        string relativePath,
        string expectedSha256,
        string actualSha256)
    {
        return new IntegrityVerificationResult(
            IntegrityVerificationStatus.Passed,
            filePath,
            relativePath,
            expectedSha256,
            actualSha256);
    }

    public static IntegrityVerificationResult Failed(
        IntegrityVerificationStatus status,
        string filePath,
        string? relativePath,
        string? expectedSha256,
        string? actualSha256)
    {
        if (status == IntegrityVerificationStatus.Passed)
        {
            throw new ArgumentException(null, nameof(status));
        }

        return new IntegrityVerificationResult(
            status,
            filePath,
            relativePath,
            expectedSha256,
            actualSha256);
    }

    private static string? NormalizeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim().Replace('\\', '/');

        if (Path.IsPathRooted(normalized)
            || normalized.Contains('\0', StringComparison.Ordinal)
            || normalized.Contains("//", StringComparison.Ordinal)
            || ContainsParentTraversalSegment(normalized))
        {
            return null;
        }

        return normalized;
    }

    private static string? NormalizeSha256(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length != 64)
        {
            return null;
        }

        foreach (char c in normalized)
        {
            bool isHex =
                c is >= '0' and <= '9'
                || c is >= 'a' and <= 'f'
                || c is >= 'A' and <= 'F';

            if (!isHex)
            {
                return null;
            }
        }

        return normalized.ToUpperInvariant();
    }

    private static bool ContainsParentTraversalSegment(string path)
    {
        string[] segments = path.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries);

        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}