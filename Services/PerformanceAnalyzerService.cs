using HakamiqChdTool.App.Models;
using Serilog;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace HakamiqChdTool.App.Services;

public sealed class PerformanceAnalyzerService
{
    private const string CpuBoundMessageKey = "LocPerformance_CpuBound";
    private const string DiskIoSuspectedMessageKey = "LocPerformance_DiskIoSuspected";
    private const string MemoryPressureMessageKey = "LocPerformance_MemoryPressure";
    private const string BalancedMessageKey = "LocPerformance_Balanced";
    private const string UnknownMessageKey = "LocPerformance_Unknown";

    private static readonly ILogger Logger = Log.ForContext<PerformanceAnalyzerService>();
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(1);

    public async Task MonitorProcessAsync(
        Process process,
        string? outputPath,
        IProgress<PerformanceSample>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (progress is null)
        {
            return;
        }

        int processorCount = Math.Max(Environment.ProcessorCount, 1);
        TimeSpan lastCpu = SafeTotalProcessorTime(process);
        long lastOutputBytes = TryGetFileLength(outputPath);
        long lastTicks = Stopwatch.GetTimestamp();
        int slowDiskHits = 0;

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(DefaultInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (SafeHasExited(process))
            {
                break;
            }

            long nowTicks = Stopwatch.GetTimestamp();
            double elapsedSeconds = Math.Max((nowTicks - lastTicks) / (double)Stopwatch.Frequency, 0.001d);
            TimeSpan currentCpu = SafeTotalProcessorTime(process);

            double cpuPercent = Math.Clamp(
                ((currentCpu - lastCpu).TotalSeconds / elapsedSeconds) * 100d / processorCount,
                0d,
                100d);

            long outputBytes = TryGetFileLength(outputPath);
            double writeBps = Math.Max(0d, (outputBytes - lastOutputBytes) / elapsedSeconds);
            long workingSet = SafeWorkingSet(process);
            long privateBytes = SafePrivateMemory(process);
            long availableMemory = TryGetAvailableMemoryBytes();

            BottleneckKind bottleneck = Classify(cpuPercent, writeBps, availableMemory, ref slowDiskHits);
            (string messageKey, IReadOnlyList<object?> messageArgs) = BuildStatusMessage(bottleneck, cpuPercent, writeBps, availableMemory);

            var sample = new PerformanceSample(
                DateTimeOffset.Now,
                SafeProcessId(process),
                cpuPercent,
                workingSet,
                privateBytes,
                availableMemory,
                outputBytes,
                writeBps,
                bottleneck,
                messageKey,
                messageArgs);

            progress.Report(sample);

            if (bottleneck == BottleneckKind.DiskIoSuspected || bottleneck == BottleneckKind.MemoryPressure)
            {
                Logger.Warning("Performance bottleneck detected. {@Sample}", sample);
            }

            lastCpu = currentCpu;
            lastOutputBytes = outputBytes;
            lastTicks = nowTicks;
        }
    }

    private static BottleneckKind Classify(double cpuPercent, double writeBps, long availableMemory, ref int slowDiskHits)
    {
        if (availableMemory > 0 && availableMemory < 768L * 1024L * 1024L)
        {
            return BottleneckKind.MemoryPressure;
        }

        if (cpuPercent < 35d && writeBps is > 0 and < 8d * 1024d * 1024d)
        {
            slowDiskHits++;
        }
        else
        {
            slowDiskHits = 0;
        }

        if (slowDiskHits >= 3)
        {
            return BottleneckKind.DiskIoSuspected;
        }

        if (cpuPercent >= 70d)
        {
            return BottleneckKind.CpuBound;
        }

        return BottleneckKind.Balanced;
    }

    private static (string MessageKey, IReadOnlyList<object?> Args) BuildStatusMessage(
        BottleneckKind kind,
        double cpuPercent,
        double writeBps,
        long availableMemory) =>
        kind switch
        {
            BottleneckKind.CpuBound => (
                CpuBoundMessageKey,
                [cpuPercent]),

            BottleneckKind.DiskIoSuspected => (
                DiskIoSuspectedMessageKey,
                [FormatBytesPerSecond(writeBps)]),

            BottleneckKind.MemoryPressure => (
                MemoryPressureMessageKey,
                [FormatBytes(availableMemory)]),

            BottleneckKind.Balanced => (
                BalancedMessageKey,
                [cpuPercent, FormatBytesPerSecond(writeBps)]),

            _ => (
                UnknownMessageKey,
                [])
        };

    private static TimeSpan SafeTotalProcessorTime(Process process)
    {
        try
        {
            process.Refresh();
            return process.TotalProcessorTime;
        }
        catch (Exception ex) when (IsExpectedProcessReadException(ex))
        {
            return TimeSpan.Zero;
        }
    }

    private static long SafeWorkingSet(Process process)
    {
        try
        {
            process.Refresh();
            return process.WorkingSet64;
        }
        catch (Exception ex) when (IsExpectedProcessReadException(ex))
        {
            return 0L;
        }
    }

    private static long SafePrivateMemory(Process process)
    {
        try
        {
            process.Refresh();
            return process.PrivateMemorySize64;
        }
        catch (Exception ex) when (IsExpectedProcessReadException(ex))
        {
            return 0L;
        }
    }

    private static bool SafeHasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (Exception ex) when (IsExpectedProcessReadException(ex))
        {
            return true;
        }
    }

    private static int SafeProcessId(Process process)
    {
        try
        {
            return process.Id;
        }
        catch (Exception ex) when (IsExpectedProcessReadException(ex))
        {
            return 0;
        }
    }

    private static long TryGetFileLength(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0L;
        }

        try
        {
            string fullPath = Path.GetFullPath(path);
            return File.Exists(fullPath) ? new FileInfo(fullPath).Length : 0L;
        }
        catch (Exception ex) when (IsExpectedPathOrFileException(ex))
        {
            return 0L;
        }
    }

    private static long TryGetAvailableMemoryBytes()
    {
        var status = new MemoryStatusEx
        {
            Length = (uint)Marshal.SizeOf<MemoryStatusEx>()
        };

        return GlobalMemoryStatusEx(ref status)
            ? status.AvailPhys > (ulong)long.MaxValue ? long.MaxValue : (long)status.AvailPhys
            : 0L;
    }

    private static string FormatBytesPerSecond(double bytesPerSecond) =>
        $"{FormatBytes((long)Math.Max(0d, bytesPerSecond))}/s";

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;

        while (value >= 1024d && unit < units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.##} {units[unit]}";
    }

    private static bool IsExpectedProcessReadException(Exception ex) =>
        ex is InvalidOperationException
        or System.ComponentModel.Win32Exception
        or NotSupportedException;

    private static bool IsExpectedPathOrFileException(Exception ex) =>
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or NotSupportedException
        or PathTooLongException;

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}