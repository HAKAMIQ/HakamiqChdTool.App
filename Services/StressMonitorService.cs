using Serilog;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text;

namespace HakamiqChdTool.App.Services;

public sealed class StressMonitorService
{
    private const int CpuRamSampleIntervalMs = 3000;
    private const int GpuSampleIntervalMs = 90_000;
    private const int MaxSessionNameLength = 80;

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<StressMonitorService>();

    public async Task RunAsync(string sessionName, CancellationToken token)
    {
        if (!TryBuildLogPath(sessionName, out string logPath))
        {
            Logger.Warning("[StressMode] Stress monitor was not started because the log path could not be prepared.");
            return;
        }

        Logger.Information("[StressMode] Started; log={LogPath}", logPath);

        using Process process = Process.GetCurrentProcess();

        DateTime lastWall = DateTime.UtcNow;
        TimeSpan lastCpu = process.TotalProcessorTime;
        double lastGpuPercent = -1;
        bool gpuSamplingEnabled = true;
        DateTime lastGpuSampleUtc = DateTime.UtcNow.AddMilliseconds(-GpuSampleIntervalMs);

        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(CpuRamSampleIntervalMs, token).ConfigureAwait(false);

                process.Refresh();

                DateTime now = DateTime.UtcNow;
                TimeSpan cpuNow = process.TotalProcessorTime;
                double cpuPercent = ComputeCpuPercent(lastCpu, cpuNow, lastWall, now);

                lastCpu = cpuNow;
                lastWall = now;

                long workingSetMb = process.WorkingSet64 / (1024 * 1024);
                long gcMb = GC.GetTotalMemory(forceFullCollection: false) / (1024 * 1024);

                if (gpuSamplingEnabled && (now - lastGpuSampleUtc).TotalMilliseconds >= GpuSampleIntervalMs)
                {
                    (lastGpuPercent, gpuSamplingEnabled) = await Task.Run(TryReadGpuUtilizationPercent, token).ConfigureAwait(false);
                    lastGpuSampleUtc = now;
                }

                string gpuDisplay = gpuSamplingEnabled && lastGpuPercent >= 0 ? $"{lastGpuPercent:0.0}%" : "N/A";
                string line =
                    $"{DateTime.Now:HH:mm:ss} | CPU {cpuPercent:0.0}% | RAM {workingSetMb} MB | GC {gcMb} MB | GPU {gpuDisplay}";

                Logger.Debug("[StressMode] {Line}", line);

                try
                {
                    await File.AppendAllTextAsync(logPath, line + Environment.NewLine, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
                {
                    Logger.Warning(ex, "[StressMode] Failed to append stress log. Path={Path}", logPath);
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        finally
        {
            Logger.Debug("[StressMode] Session ended. Log={LogPath}", logPath);
        }
    }

    private static bool TryBuildLogPath(string sessionName, out string logPath)
    {
        logPath = string.Empty;

        try
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HakamiqChdTool",
                "Logs");

            Directory.CreateDirectory(folder);

            string safe = Sanitize(sessionName);
            logPath = Path.Combine(folder, $"stress_{DateTime.Now:yyyyMMdd_HHmmss}_{safe}.log");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Logger.Warning(ex, "[StressMode] Failed to prepare stress log path.");
            return false;
        }
    }

    private static string Sanitize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "session";
        }

        char[] invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(Math.Min(value.Length, MaxSessionNameLength));

        foreach (char c in value.Trim())
        {
            if (sb.Length >= MaxSessionNameLength)
            {
                break;
            }

            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        string result = sb.ToString().Trim().TrimEnd('.', ' ');
        return result.Length == 0 ? "session" : result;
    }

    private static double ComputeCpuPercent(TimeSpan prevCpu, TimeSpan nowCpu, DateTime prevWall, DateTime nowWall)
    {
        double wallMs = (nowWall - prevWall).TotalMilliseconds;
        if (wallMs <= 0)
        {
            return 0;
        }

        double cpuMs = (nowCpu - prevCpu).TotalMilliseconds;
        double logical = Math.Max(1, Environment.ProcessorCount);
        return Math.Clamp((cpuMs / (wallMs * logical)) * 100.0, 0, 100);
    }

    private static (double Percent, bool KeepSampling) TryReadGpuUtilizationPercent()
    {
        try
        {
            double max = 0;
            int count = 0;

            using var searcher = new ManagementObjectSearcher(
                "SELECT UtilizationPercentage FROM Win32_PerfFormattedData_GPUPerformanceCounters_GPUEngine");

            foreach (ManagementObject obj in searcher.Get())
            {
                using (obj)
                {
                    if (obj["UtilizationPercentage"] is uint u)
                    {
                        max = Math.Max(max, u);
                        count++;
                    }
                }
            }

            if (count == 0)
            {
                Logger.Debug("[StressMode] GPU utilization counters are unavailable; disabling GPU sampling for this session.");
                return (-1, false);
            }

            return (Math.Clamp(max, 0, 100), true);
        }
        catch (Exception ex) when (ex is ManagementException or UnauthorizedAccessException or InvalidOperationException)
        {
            Logger.Debug(ex, "[StressMode] GPU utilization WMI query failed; disabling GPU sampling for this session.");
            return (-1, false);
        }
    }
}