using Serilog;
using System;
using System.IO;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;
using System.Threading;

namespace HakamiqChdTool.App.Services;

public sealed class RuntimeToolService
{
    private const string BundledChdmanRelativePath = @"Tools\chdman.exe";
    private const string BundledChdmanSha256Hex = "8A74468E3B0879698835B57C3B58E88E5A51E4DE73BEE6EF755C28530B5B040F";

    private const string BundledToolUnsafeMessageKey = "LocRuntimeTools_BundledToolUnsafe";
    private const string BundledToolMissingMessageKey = "LocRuntimeTools_BundledToolMissing";
    private const string BundledToolInvalidMessageKey = "LocRuntimeTools_BundledToolInvalid";
    private const string UnsupportedCpuMessageKey = "LocRuntimeTools_UnsupportedCpu";

    private static readonly ILogger Logger = global::Serilog.Log.ForContext<RuntimeToolService>();
    private static readonly byte[] BundledChdmanSha256 = Convert.FromHexString(BundledChdmanSha256Hex);

    private readonly object _sync = new();

    private bool _initialized;
    private string _chdmanPath = string.Empty;

    public static RuntimeToolService Instance { get; } = new();

    private RuntimeToolService()
    {
    }

    public void EnsureInitialized()
    {
        if (Volatile.Read(ref _initialized))
        {
            return;
        }

        lock (_sync)
        {
            if (_initialized)
            {
                return;
            }

            if (!IsX8664V2Supported())
            {
                Logger.Error("RuntimeTools: the processor does not satisfy the x86-64-v2 requirement of bundled chdman 0.289.");
                throw new PlatformNotSupportedException(UnsupportedCpuMessageKey);
            }

            _chdmanPath = ResolveBundledChdmanPath();
            ValidateBundledChdman(_chdmanPath);

            Logger.Information("RuntimeTools: bundled chdman ready. Path={Path}", _chdmanPath);
            Volatile.Write(ref _initialized, true);
        }
    }

    public string GetChdmanPath()
    {
        EnsureInitialized();
        ValidateBundledChdman(_chdmanPath);
        return _chdmanPath;
    }

    private static string ResolveBundledChdmanPath()
    {
        string applicationBase = Path.GetFullPath(AppContext.BaseDirectory);
        string toolsDirectory = Path.GetFullPath(Path.Combine(applicationBase, "Tools"));
        string chdmanPath = Path.GetFullPath(Path.Combine(applicationBase, BundledChdmanRelativePath));
        string? chdmanDirectory = Path.GetDirectoryName(chdmanPath);

        if (!Directory.Exists(applicationBase)
            || string.IsNullOrWhiteSpace(chdmanDirectory)
            || !PathsEqual(chdmanDirectory, toolsDirectory)
            || !IsStrictlyUnder(chdmanPath, applicationBase)
            || !string.Equals(Path.GetFileName(chdmanPath), "chdman.exe", StringComparison.OrdinalIgnoreCase)
            || HasReparsePointInExistingPathFromVolumeRoot(chdmanPath))
        {
            throw new InvalidOperationException(BundledToolUnsafeMessageKey);
        }

        return chdmanPath;
    }

    private static void ValidateBundledChdman(string chdmanPath)
    {
        string applicationBase = Path.GetFullPath(AppContext.BaseDirectory);
        string expectedPath = Path.GetFullPath(Path.Combine(applicationBase, BundledChdmanRelativePath));
        string fullPath = Path.GetFullPath(chdmanPath);

        if (!PathsEqual(fullPath, expectedPath)
            || !IsStrictlyUnder(fullPath, applicationBase)
            || !string.Equals(Path.GetFileName(fullPath), "chdman.exe", StringComparison.OrdinalIgnoreCase)
            || HasReparsePointInExistingPathFromVolumeRoot(fullPath))
        {
            throw new InvalidOperationException(BundledToolUnsafeMessageKey);
        }

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(BundledToolMissingMessageKey, fullPath);
        }

        FileInfo fileInfo = new(fullPath);
        if (fileInfo.Length <= 0)
        {
            throw new InvalidOperationException(BundledToolInvalidMessageKey);
        }

        byte[] actualSha256;
        using (FileStream stream = new(
                   fullPath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.Read,
                   bufferSize: 1024 * 1024,
                   FileOptions.SequentialScan))
        {
            actualSha256 = SHA256.HashData(stream);
        }

        if (!CryptographicOperations.FixedTimeEquals(BundledChdmanSha256, actualSha256))
        {
            Logger.Error("RuntimeTools: bundled chdman content hash does not match the pinned SHA-256 digest.");
            throw new InvalidOperationException(BundledToolInvalidMessageKey);
        }
    }

    private static bool IsX8664V2Supported()
    {
        if (!Environment.Is64BitProcess || !X86Base.IsSupported || !X86Base.X64.IsSupported)
        {
            return false;
        }

        var leaf1 = X86Base.CpuId(1, 0);
        const int requiredLeaf1Ecx = (1 << 0)   // SSE3
            | (1 << 9)                         // SSSE3
            | (1 << 13)                        // CMPXCHG16B
            | (1 << 19)                        // SSE4.1
            | (1 << 20)                        // SSE4.2
            | (1 << 23);                       // POPCNT

        if ((leaf1.Ecx & requiredLeaf1Ecx) != requiredLeaf1Ecx)
        {
            return false;
        }

        var maximumExtended = X86Base.CpuId(unchecked((int)0x80000000), 0);
        if ((uint)maximumExtended.Eax < 0x80000001u)
        {
            return false;
        }

        var extendedFeatures = X86Base.CpuId(unchecked((int)0x80000001), 0);
        return (extendedFeatures.Ecx & 1) != 0; // LAHF/SAHF in 64-bit mode
    }

    private static bool HasReparsePointInExistingPathFromVolumeRoot(string candidatePath)
    {
        try
        {
            string candidate = Path.GetFullPath(candidatePath);
            string? root = Path.GetPathRoot(candidate);

            if (string.IsNullOrWhiteSpace(root))
            {
                return true;
            }

            string current = candidate;
            while (true)
            {
                if ((File.Exists(current) || Directory.Exists(current))
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    return true;
                }

                if (PathsEqual(current, root))
                {
                    return false;
                }

                string? parent = Directory.GetParent(current)?.FullName;
                if (string.IsNullOrWhiteSpace(parent) || PathsEqual(parent, current))
                {
                    return true;
                }

                current = parent;
            }
        }
        catch (Exception ex) when (ex is IOException
                                   or UnauthorizedAccessException
                                   or ArgumentException
                                   or NotSupportedException
                                   or PathTooLongException
                                   or System.Security.SecurityException)
        {
            return true;
        }
    }

    private static bool IsStrictlyUnder(string candidatePath, string rootPath)
    {
        string candidate = TrimDirectorySeparators(Path.GetFullPath(candidatePath));
        string root = TrimDirectorySeparators(Path.GetFullPath(rootPath));

        return !string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
            && candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            TrimDirectorySeparators(Path.GetFullPath(left)),
            TrimDirectorySeparators(Path.GetFullPath(right)),
            StringComparison.OrdinalIgnoreCase);

    private static string TrimDirectorySeparators(string path)
    {
        string? root = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(root) && path.Length <= root.Length)
        {
            return root;
        }

        string trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.IsNullOrEmpty(trimmed) && !string.IsNullOrWhiteSpace(root)
            ? root
            : trimmed;
    }
}
