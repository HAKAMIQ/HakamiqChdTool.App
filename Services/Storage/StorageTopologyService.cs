using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services.Storage;

internal sealed record StorageTopologySnapshot(
    string SourceRoot,
    string PendingRoot,
    string FinalOutputRoot,
    bool SourceAndPendingSameVolume,
    bool PendingAndFinalOutputSameVolume,
    bool SourceAndFinalOutputSameVolume,
    bool SourceIsExternal,
    bool PendingIsExternal,
    bool OutputIsExternal,
    StorageDeviceIdentity SourceDevice,
    StorageDeviceIdentity PendingDevice,
    StorageDeviceIdentity OutputDevice);

internal sealed class StorageTopologyService
{
    private readonly StorageDeviceResolver _deviceResolver;
    private readonly ILogger _log;

    public StorageTopologyService()
        : this(new StorageDeviceResolver(), Log.ForContext<StorageTopologyService>())
    {
    }

    public StorageTopologyService(StorageDeviceResolver deviceResolver, ILogger log)
    {
        _deviceResolver = deviceResolver ?? throw new ArgumentNullException(nameof(deviceResolver));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public StorageTopologySnapshot Analyze(
        string sourcePath,
        string pendingOutputPath,
        string finalOutputPath)
    {
        StorageDeviceIdentity source = _deviceResolver.Resolve(sourcePath);
        StorageDeviceIdentity pending = _deviceResolver.Resolve(ResolveDirectoryCandidate(pendingOutputPath));
        StorageDeviceIdentity output = _deviceResolver.Resolve(ResolveDirectoryCandidate(finalOutputPath));

        bool sourceAndPending = AreSameVolume(source, pending);
        bool pendingAndOutput = AreSameVolume(pending, output);
        bool sourceAndOutput = AreSameVolume(source, output);

        _log.Information(
            "Storage topology resolved. SourceRoot={SourceRoot}, PendingRoot={PendingRoot}, OutputRoot={OutputRoot}, SourceAndPendingSameVolume={SourceAndPendingSameVolume}, PendingAndOutputSameVolume={PendingAndOutputSameVolume}, SourceAndOutputSameVolume={SourceAndOutputSameVolume}, SourceExternal={SourceExternal}, PendingExternal={PendingExternal}, OutputExternal={OutputExternal}",
            source.VolumeRoot,
            pending.VolumeRoot,
            output.VolumeRoot,
            sourceAndPending,
            pendingAndOutput,
            sourceAndOutput,
            source.IsExternalOrRemovable,
            pending.IsExternalOrRemovable,
            output.IsExternalOrRemovable);

        return new StorageTopologySnapshot(
            source.VolumeRoot,
            pending.VolumeRoot,
            output.VolumeRoot,
            sourceAndPending,
            pendingAndOutput,
            sourceAndOutput,
            source.IsExternalOrRemovable,
            pending.IsExternalOrRemovable,
            output.IsExternalOrRemovable,
            source,
            pending,
            output);
    }

    private static bool AreSameVolume(StorageDeviceIdentity left, StorageDeviceIdentity right)
    {
        if (!string.IsNullOrWhiteSpace(left.PhysicalDrivePath)
            && !string.IsNullOrWhiteSpace(right.PhysicalDrivePath)
            && string.Equals(left.PhysicalDrivePath, right.PhysicalDrivePath, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(left.VolumeRoot)
            && !string.IsNullOrWhiteSpace(right.VolumeRoot)
            && string.Equals(left.VolumeRoot, right.VolumeRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDirectoryCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            string fullPath = Path.GetFullPath(path.Trim());
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }

            return Path.GetDirectoryName(fullPath) ?? fullPath;
        }
        catch (Exception ex) when (
            ex is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException
            or PathTooLongException
            or System.Security.SecurityException)
        {
            return path;
        }
    }
}

