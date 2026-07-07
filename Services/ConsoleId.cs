using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services;

public sealed record ConsoleIdResult(
    string InputPath,
    string PlatformName,
    string Reason,
    int ConfidenceScore)
{
    public bool IsIdentified =>
        !string.IsNullOrWhiteSpace(PlatformName)
        && ConfidenceScore >= 75
        && PlatformDetectionService.IsActionablePlatformName(PlatformName);
}

public sealed class ConsoleIdBg : IDisposable
{
    private static readonly TimeSpan DetectionTimeout = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<ConsoleIdCacheKey, ConsoleIdResult> cache = new();
    private readonly SemaphoreSlim gate = new(2, 2);
    private readonly CancellationTokenSource shutdown = new();

    public void Enqueue(
        Guid itemId,
        string path,
        string currentPlatform,
        string currentReason,
        Action<Guid, ConsoleIdResult> apply)
    {
        ArgumentNullException.ThrowIfNull(apply);

        if (itemId == Guid.Empty || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (PlatformDetectionService.IsActionablePlatformName(currentPlatform))
        {
            return;
        }

        _ = Task.Run(
            () => RunAsync(itemId, path, apply, shutdown.Token),
            CancellationToken.None);
    }

    public void Dispose()
    {
        shutdown.Cancel();
        gate.Dispose();
        shutdown.Dispose();
    }

    private async Task RunAsync(
        Guid itemId,
        string path,
        Action<Guid, ConsoleIdResult> apply,
        CancellationToken cancellationToken)
    {
        try
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

            try
            {
                ConsoleIdResult result = await ResolveCachedAsync(path, cancellationToken).ConfigureAwait(false);
                if (result.IsIdentified)
                {
                    apply(itemId, result);
                }
            }
            finally
            {
                gate.Release();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task<ConsoleIdResult> ResolveCachedAsync(string path, CancellationToken cancellationToken)
    {
        if (!TryBuildCacheKey(path, out ConsoleIdCacheKey key))
        {
            return ConsoleIdResultFactory.Unknown(path);
        }

        if (cache.TryGetValue(key, out ConsoleIdResult? cached))
        {
            return cached;
        }

        ConsoleIdResult result = await ConsoleIdSvc.DetectAsync(path, DetectionTimeout, cancellationToken).ConfigureAwait(false);

        if (result.IsIdentified)
        {
            cache[key] = result;
        }

        return result;
    }

    private static bool TryBuildCacheKey(string path, out ConsoleIdCacheKey key)
    {
        key = default;

        try
        {
            string fullPath = Path.GetFullPath(path);
            var info = new FileInfo(fullPath);
            if (!info.Exists)
            {
                return false;
            }

            key = new ConsoleIdCacheKey(
                fullPath,
                info.Length,
                info.LastWriteTimeUtc.Ticks);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private readonly record struct ConsoleIdCacheKey(
        string FullPath,
        long Length,
        long LastWriteUtcTicks);
}

internal static class ConsoleIdSvc
{
    public static async Task<ConsoleIdResult> DetectAsync(
        string path,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Task.Run(
                    () => Detect(path),
                    cancellationToken)
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch
        {
            return ConsoleIdResultFactory.Unknown(path);
        }
    }

    private static ConsoleIdResult Detect(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return ConsoleIdResultFactory.Unknown(path);
        }

        ConsoleIdResult extensionResult = DetectFromExtension(path);
        if (extensionResult.IsIdentified)
        {
            return extensionResult;
        }

        try
        {
            PlatformDetectionResult platform = PlatformDetectionService.Detect(path);
            if (PlatformDetectionService.IsActionablePlatformName(platform.PlatformName)
                && platform.ConfidenceScore >= 75)
            {
                return new ConsoleIdResult(
                    path,
                    platform.PlatformName.Trim(),
                    platform.Reason,
                    platform.ConfidenceScore);
            }
        }
        catch
        {
        }

        ConsoleIdResult pathHintResult = DetectFromPathHint(path);
        return pathHintResult.IsIdentified
            ? pathHintResult
            : ConsoleIdResultFactory.Unknown(path);
    }

    private static ConsoleIdResult DetectFromExtension(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        return extension switch
        {
            ".cso" => new ConsoleIdResult(path, "PlayStation Portable", "LocPlatformDetect_CsoExtension", 95),
            ".gdi" => new ConsoleIdResult(path, "SEGA Dreamcast", "LocPlatformDetect_GdiExtension", 95),
            _ => ConsoleIdResultFactory.Unknown(path)
        };
    }

    private static ConsoleIdResult DetectFromPathHint(string path)
    {
        if (ConsoleAlias.TryDetect(path, out ConsoleIdResult redumpAliasResult))
        {
            return redumpAliasResult;
        }

        string normalized = Normalize(path);

        bool Has(params string[] tokens)
        {
            foreach (string token in tokens)
            {
                if (normalized.Contains($" {Normalize(token).Trim()} ", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        if (Has("playstation 3", "sony playstation 3", "ps3", "ps3 bd", "ps3 game"))
        {
            return new ConsoleIdResult(path, "PlayStation 3", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("playstation 2", "sony playstation 2", "ps2"))
        {
            return new ConsoleIdResult(path, "PlayStation 2", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("playstation 1", "sony playstation 1", "ps1", "psx"))
        {
            return new ConsoleIdResult(path, "PlayStation 1", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("playstation portable", "sony psp", "psp"))
        {
            return new ConsoleIdResult(path, "PlayStation Portable", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("dreamcast", "sega dreamcast"))
        {
            return new ConsoleIdResult(path, "SEGA Dreamcast", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("saturn", "sega saturn"))
        {
            return new ConsoleIdResult(path, "SEGA Saturn", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("gamecube", "game cube", "ngc"))
        {
            return new ConsoleIdResult(path, "Nintendo GameCube", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("wii u", "wiiu", "nintendo wii u", "nintendo wiiu"))
        {
            return new ConsoleIdResult(path, "Nintendo Wii U", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("wii", "nintendo wii"))
        {
            return new ConsoleIdResult(path, "Nintendo Wii", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("xbox series x", "xbox series s", "xbox series", "xboxsx"))
        {
            return new ConsoleIdResult(path, "Xbox Series X", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("xbox one", "xboxone"))
        {
            return new ConsoleIdResult(path, "Xbox One", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("xbox 360", "xbox360", "x360"))
        {
            return new ConsoleIdResult(path, "Xbox 360", "LocPlatformDetect_PathHint", 78);
        }

        if (Has("xbox", "microsoft xbox", "original xbox"))
        {
            return new ConsoleIdResult(path, "Xbox", "LocPlatformDetect_PathHint", 76);
        }

        return ConsoleIdResultFactory.Unknown(path);
    }

    private static string Normalize(string value)
    {
        var builder = new StringBuilder((value?.Length ?? 0) + 2);
        builder.Append(' ');

        bool previousWasSpace = true;

        foreach (char character in (value ?? string.Empty).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
                previousWasSpace = false;
                continue;
            }

            if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        if (!previousWasSpace)
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }
}

internal static class ConsoleIdResultFactory
{
    public static ConsoleIdResult Unknown(string path) => new(
        path ?? string.Empty,
        string.Empty,
        string.Empty,
        0);
}