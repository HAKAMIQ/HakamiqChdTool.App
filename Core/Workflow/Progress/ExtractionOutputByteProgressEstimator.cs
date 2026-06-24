using HakamiqChdTool.App.Core.Workflow.Extraction;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Core.Workflow.Progress;

internal sealed class ExtractionOutputByteProgressEstimator : IWorkflowRuntimeProgressEstimator
{
    private readonly ExtractionOutputContract _contract;
    private readonly long _expectedBytes;
    private readonly object _gate = new();

    private long _lastBytes;
    private long _lastTicks;

    public ExtractionOutputByteProgressEstimator(
        ExtractionOutputContract contract,
        long expectedBytes)
    {
        _contract = contract ?? throw new ArgumentNullException(nameof(contract));
        _expectedBytes = Math.Max(0L, expectedBytes);
        _lastTicks = Stopwatch.GetTimestamp();
    }

    public WorkflowRuntimeProgressSample Capture()
    {
        long currentBytes = CaptureCurrentBytes();
        long nowTicks = Stopwatch.GetTimestamp();
        double bytesPerSecond;

        lock (_gate)
        {
            double elapsedSeconds = Math.Max((nowTicks - _lastTicks) / (double)Stopwatch.Frequency, 0.001d);
            bytesPerSecond = Math.Max(0d, (currentBytes - _lastBytes) / elapsedSeconds);
            _lastBytes = currentBytes;
            _lastTicks = nowTicks;
        }

        double? percent = null;
        if (_expectedBytes > 0 && currentBytes > 0)
        {
            percent = Math.Clamp(currentBytes * 100d / _expectedBytes, 1d, 99d);
        }

        WorkflowRuntimeProgressMode mode = currentBytes > 0
            ? WorkflowRuntimeProgressMode.EstimatedFromOutputBytes
            : WorkflowRuntimeProgressMode.Indeterminate;

        return new WorkflowRuntimeProgressSample(
            mode,
            currentBytes,
            _expectedBytes,
            percent,
            bytesPerSecond,
            DateTimeOffset.Now);
    }

    private long CaptureCurrentBytes() => _contract.Kind switch
    {
        ExtractionOutputKind.SingleFile => TryGetFileLength(_contract.PendingPrimaryPath),
        ExtractionOutputKind.CueBinBundle => TryGetCueBinWorkspaceBytes(_contract.PendingPrimaryPath),
        _ => 0L
    };

    private static long TryGetCueBinWorkspaceBytes(string pendingCuePath)
    {
        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(pendingCuePath));
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            {
                return 0L;
            }

            return Directory
                .EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly)
                .Where(static path =>
                    string.Equals(Path.GetExtension(path), ".cue", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
                .Sum(static path => TryGetFileLength(path));
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return 0L;
        }
    }

    private static long TryGetFileLength(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return 0L;
            }

            return Math.Max(0L, new FileInfo(path).Length);
        }
        catch (Exception ex) when (IsExpectedFileSystemException(ex))
        {
            return 0L;
        }
    }

    private static bool IsExpectedFileSystemException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException
        or InvalidOperationException
        or System.Security.SecurityException;
}
