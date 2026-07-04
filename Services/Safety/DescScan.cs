using HakamiqChdTool.App.Core.Disc;
using HakamiqChdTool.App.Models;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Safety;

public sealed partial class DescriptorSafetyScanner
{
    private const int MaxDescriptorBytes = 256 * 1024;
    private const int RegexTimeoutMilliseconds = 250;

    private readonly ILogger _logger;
    private readonly SafetyPathPolicy _pathPolicy;

    public DescriptorSafetyScanner()
        : this(Log.ForContext<DescriptorSafetyScanner>(), SafetyPathPolicy.Shared)
    {
    }

    internal DescriptorSafetyScanner(ILogger logger, SafetyPathPolicy pathPolicy)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pathPolicy = pathPolicy ?? throw new ArgumentNullException(nameof(pathPolicy));
    }

    public async Task<InputSafetyScanResult> ScanAsync(
        string descriptorPath,
        InputSafetyPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (!_pathPolicy.TryGetExistingFilePath(descriptorPath, out string fullDescriptorPath))
        {
            return InputSafetyScanResult.Empty;
        }

        string extension = Path.GetExtension(fullDescriptorPath);
        if (!IsDescriptorExtension(extension))
        {
            return InputSafetyScanResult.Empty;
        }

        string descriptorText;
        try
        {
            descriptorText = await ReadSmallDescriptorTextAsync(fullDescriptorPath, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (IsExpectedDescriptorException(ex))
        {
            _logger.Debug(ex, "Descriptor safety scanner could not read descriptor. Descriptor={Descriptor}", fullDescriptorPath);
            return InputSafetyScanResult.Empty;
        }

        string[] references;
        try
        {
            references = [.. ExtractReferences(extension, descriptorText)];
        }
        catch (Exception ex) when (IsExpectedDescriptorException(ex))
        {
            _logger.Debug(ex, "Descriptor safety scanner could not parse descriptor references. Descriptor={Descriptor}", fullDescriptorPath);
            return InputSafetyScanResult.Empty;
        }

        var artifacts = new List<SuspiciousArtifact>();

        foreach (string reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!IsSafeDescriptorReference(reference))
            {
                artifacts.Add(new SuspiciousArtifact(
                    fullDescriptorPath,
                    reference,
                    SuspiciousArtifactKind.UnsafeDescriptorReference,
                    QueueIntakeAdvisorySeverity.Blocker,
                    "LocInputSafety_UnsafeDescriptorReference"));

                if (artifacts.Count >= policy.MaxArtifacts)
                {
                    break;
                }
            }
        }

        return InputSafetyScanResult.FromArtifacts(artifacts);
    }

    private async Task<string> ReadSmallDescriptorTextAsync(
        string descriptorPath,
        CancellationToken cancellationToken)
    {
        if (!_pathPolicy.TryGetExistingFilePath(descriptorPath, out string fullPath))
        {
            throw new IOException();
        }

        FileInfo fileInfo = new(fullPath);
        if (!fileInfo.Exists || fileInfo.Length <= 0 || fileInfo.Length > MaxDescriptorBytes)
        {
            throw new InvalidDataException();
        }

        await using FileStream stream = new(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.SequentialScan);

        using StreamReader reader = new(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        char[] buffer = new char[MaxDescriptorBytes];
        int read = await reader.ReadBlockAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
            .ConfigureAwait(false);

        return new string(buffer, 0, read);
    }

    private static IEnumerable<string> ExtractReferences(string extension, string descriptorText)
    {
        if (string.IsNullOrWhiteSpace(descriptorText))
        {
            yield break;
        }

        if (extension.Equals(".cue", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string line in descriptorText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (CueSheetFileStatementReader.TryRead(line, out string reference, out _))
                {
                    yield return reference;
                }
            }

            yield break;
        }

        if (extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase))
        {
            foreach (string rawLine in descriptorText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || !char.IsDigit(line[0]))
                {
                    continue;
                }

                Match quoted = GdiQuotedFileRegex().Match(line);
                if (quoted.Success)
                {
                    yield return quoted.Groups["file"].Value;
                    continue;
                }

                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5)
                {
                    yield return parts[4];
                }
            }

            yield break;
        }

        if (extension.Equals(".toc", StringComparison.OrdinalIgnoreCase))
        {
            foreach (Match match in TocFileRegex().Matches(descriptorText))
            {
                yield return match.Groups["file"].Value;
            }
        }
    }

    private static bool IsSafeDescriptorReference(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)
            || reference.Contains('\0'))
        {
            return false;
        }

        string raw = reference.Trim();
        string normalized = raw.Replace('\\', '/');

        if (normalized.StartsWith('@')
            || normalized.StartsWith('/')
            || normalized.StartsWith("//", StringComparison.Ordinal)
            || raw.StartsWith("\\\\", StringComparison.Ordinal)
            || Path.IsPathRooted(raw)
            || normalized.Contains(':'))
        {
            return false;
        }

        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (string segment in segments)
        {
            if (string.Equals(segment, "..", StringComparison.Ordinal)
                || string.Equals(segment, ".", StringComparison.Ordinal))
            {
                return false;
            }
        }

        return segments.Length > 0;
    }

    private static bool IsDescriptorExtension(string extension)
    {
        return extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".toc", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsExpectedDescriptorException(Exception ex)
    {
        return ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidDataException
            or NotSupportedException
            or PathTooLongException
            or InvalidOperationException
            or RegexMatchTimeoutException
            or System.Security.SecurityException;
    }


    [GeneratedRegex(
        "\"(?<file>[^\"]+)\"",
        RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex GdiQuotedFileRegex();

    [GeneratedRegex(
        "^\\s*(?:FILE|AUDIOFILE|DATAFILE)\\s+(?:\"(?<file>[^\"]+)\"|(?<file>\\S+))",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant,
        RegexTimeoutMilliseconds)]
    private static partial Regex TocFileRegex();
}
