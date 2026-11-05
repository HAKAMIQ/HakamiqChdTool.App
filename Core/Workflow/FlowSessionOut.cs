using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace HakamiqChdTool.App.Core.Workflow;

/// <summary>
/// Final outputs produced in this app session, with the source that produced each one. Overwrite mode replaces outputs
/// from earlier runs; it must not let a different source replace an output another job produced in this session, because
/// that job may already have deleted its own source (two sources mapping to one name under a custom output root).
/// </summary>
internal static class WorkflowSessionOutputRegistry
{
    private static readonly Dictionary<string, string> ProducerByOutput = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    internal static Action<string>? BeforePromotionReservationForTests;
    internal static Action<string>? BeforeMutexWaitForTests;

    /// <summary>Serializes the final collision decision, promotion and ownership record across processes.</summary>
    public static bool TryPromote(
        string pendingPath,
        string finalOutputPath,
        string sourcePath,
        bool allowOverwrite,
        CancellationToken cancellationToken)
    {
        if (!TryNormalize(finalOutputPath, out string output)
            || !TryNormalize(sourcePath, out string source))
        {
            throw new InvalidOperationException("LocConversion_InvalidOutputPath");
        }

        return TryPublish(output, source, cancellationToken, () =>
        {
            if (allowOverwrite)
            {
                WorkflowOutputPathContract.PromoteProducedFileToFinalLocation(pendingPath, output);
                return true;
            }

            return WorkflowOutputPathContract.TryPromoteProducedFileIfAbsent(pendingPath, output);
        });
    }

    public static bool TryPublish(
        string finalOutputPath,
        string sourcePath,
        CancellationToken cancellationToken,
        Func<bool> publish)
    {
        ArgumentNullException.ThrowIfNull(publish);
        if (!TryNormalize(finalOutputPath, out string output)
            || !TryNormalize(sourcePath, out string source))
        {
            throw new InvalidOperationException("LocConversion_InvalidOutputPath");
        }

        string mutexName = "Global\\HakamiqChdTool.Output." + Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(output.ToUpperInvariant())));
        using var mutex = new Mutex(false, mutexName);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                BeforeMutexWaitForTests?.Invoke(source);
                if (!mutex.WaitOne(TimeSpan.FromMilliseconds(100)))
                {
                    continue;
                }
            }
            catch (AbandonedMutexException)
            {
                // The previous owner exited; this thread owns the mutex and can safely re-evaluate the path.
            }

            try
            {
                lock (Gate)
                {
                    if (ProducerByOutput.TryGetValue(output, out string? producer)
                        && !string.Equals(producer, source, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }

                if (!publish())
                {
                    return false;
                }

                lock (Gate)
                {
                    ProducerByOutput[output] = source;
                }
                return true;
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }

    public static bool IsProducedByOtherSource(string finalOutputPath, string sourcePath)
    {
        if (!TryNormalize(finalOutputPath, out string output) || !TryNormalize(sourcePath, out string source))
        {
            return false;
        }

        lock (Gate)
        {
            return ProducerByOutput.TryGetValue(output, out string? producer)
                && !string.Equals(producer, source, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static bool TryNormalize(string path, out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
