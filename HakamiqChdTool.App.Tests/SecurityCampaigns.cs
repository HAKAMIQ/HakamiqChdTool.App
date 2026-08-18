using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private const int MaximumFuzzInputBytes = 64 * 1024;
    private const int DefaultExternalFuzzCases = 24;
    private static readonly JsonSerializerOptions SecurityJsonOptions = new()
    {
        WriteIndented = true
    };

    private static int RunSecurityCampaign(
        string mode,
        string[] args,
        AppReflection app,
        string appDirectory)
    {
        string outputRoot = Path.GetFullPath(
            ReadOptionalArgument(args, "--security-output")
            ?? Path.Combine(Environment.CurrentDirectory, "TestResults", "Security"));
        Directory.CreateDirectory(outputRoot);

        try
        {
            return mode.Trim().ToLowerInvariant() switch
            {
                "fuzz" => RunFuzzCampaign(args, app, appDirectory, outputRoot),
                "soak" => RunSoakCampaign(args, app, outputRoot),
                "corpus" => RunCorpusCampaign(args, app, appDirectory, outputRoot),
                "wpf-shutdown" => RunWpfShutdownCampaign(args, appDirectory, outputRoot),
                _ => throw new ArgumentException(
                    "Unsupported security mode. Expected fuzz, soak, corpus, or wpf-shutdown.")
            };
        }
        catch (Exception ex)
        {
            string failurePath = Path.Combine(outputRoot, "campaign-failure.json");
            WriteJsonAtomically(
                failurePath,
                new
                {
                    format = "HakamiqSecurityCampaignFailure.v1",
                    mode,
                    failedAtUtc = DateTimeOffset.UtcNow,
                    exceptionType = ex.GetType().FullName,
                    ex.Message,
                    ex.StackTrace
                });
            Console.Error.WriteLine("[FAIL] Security campaign: " + mode);
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static int RunFuzzCampaign(
        string[] args,
        AppReflection app,
        string appDirectory,
        string outputRoot)
    {
        int caseCount = ReadBoundedInt(args, "--cases", 1_000, 1, 1_000_000);
        int externalCases = ReadBoundedInt(
            args,
            "--external-cases",
            Math.Min(DefaultExternalFuzzCases, Math.Max(1, caseCount / 20)),
            0,
            10_000);
        int seed = ReadBoundedInt(args, "--seed", 0x484354, 0, int.MaxValue);

        string campaignRoot = Path.Combine(outputRoot, "fuzz");
        string activeRoot = Path.Combine(campaignRoot, "active");
        string crashRoot = Path.Combine(campaignRoot, "crashes");
        Directory.CreateDirectory(activeRoot);
        Directory.CreateDirectory(crashRoot);

        string workRoot = Path.Combine(
            Path.GetTempPath(),
            "HakamiqChdTool.SecurityFuzz",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        var random = new Random(seed);
        int completed = 0;
        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            for (int index = 0; index < caseCount; index++)
            {
                string target = (index % 3) switch
                {
                    0 => "chd-header",
                    1 => "cue-descriptor",
                    _ => "sevenzip-list"
                };
                byte[] input = CreateMutatedInput(GetManagedFuzzSeed(target), random);
                string activePath = Path.Combine(activeRoot, $"{target}-{index:D8}.bin");
                File.WriteAllBytes(activePath, input);

                try
                {
                    ExecuteManagedFuzzCase(target, input, app, workRoot, index);
                    File.Delete(activePath);
                    completed++;
                }
                catch (Exception ex)
                {
                    PreserveFuzzFailure(activePath, crashRoot, target, index, seed, ex);
                    throw new InvalidOperationException(
                        $"Managed fuzz target '{target}' failed at case {index}. Input was preserved.",
                        ex);
                }
            }

            for (int index = 0; index < externalCases; index++)
            {
                string target = index % 2 == 0 ? "csokit-container" : "sevenzip-archive";
                byte[] input = CreateMutatedInput(GetExternalFuzzSeed(target), random);
                string extension = target == "csokit-container" ? ".cso" : ".zip";
                string activePath = Path.Combine(activeRoot, $"{target}-{index:D8}{extension}");
                File.WriteAllBytes(activePath, input);

                try
                {
                    ProcessRunResult result = ExecuteExternalFuzzCase(
                        target,
                        activePath,
                        appDirectory,
                        TimeSpan.FromSeconds(15));

                    if (result.TimedOut || result.ExitCode < 0)
                    {
                        throw new InvalidOperationException(
                            $"Native target did not reject the input safely. ExitCode={result.ExitCode}; TimedOut={result.TimedOut}.");
                    }

                    File.Delete(activePath);
                    completed++;
                }
                catch (Exception ex)
                {
                    PreserveFuzzFailure(activePath, crashRoot, target, index, seed, ex);
                    throw new InvalidOperationException(
                        $"External fuzz target '{target}' failed at case {index}. Input was preserved.",
                        ex);
                }
            }

            stopwatch.Stop();
            string reportPath = Path.Combine(campaignRoot, "fuzz-report.json");
            WriteJsonAtomically(
                reportPath,
                new
                {
                    format = "HakamiqMutationFuzzReport.v1",
                    completedAtUtc = DateTimeOffset.UtcNow,
                    seed,
                    managedCases = caseCount,
                    externalCases,
                    totalCompleted = completed,
                    elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                    maximumInputBytes = MaximumFuzzInputBytes,
                    crashCount = Directory.EnumerateFiles(crashRoot, "*.bin").Count()
                });

            Console.WriteLine(
                $"[PASS] Security fuzz campaign completed: {completed}/{caseCount + externalCases}; seed={seed}; report={reportPath}");
            return 0;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private static void ExecuteManagedFuzzCase(
        string target,
        byte[] input,
        AppReflection app,
        string workRoot,
        int index)
    {
        switch (target)
        {
            case "chd-header":
            {
                string path = Path.Combine(workRoot, $"fuzz-{index:D8}.chd");
                File.WriteAllBytes(path, input);
                _ = app.ClassifyMediaInput(path);
                File.Delete(path);
                break;
            }
            case "cue-descriptor":
            {
                string directory = Path.Combine(workRoot, $"cue-{index:D8}");
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, "track.bin"), [0, 1, 2, 3]);
                string path = Path.Combine(directory, "disc.cue");
                File.WriteAllText(path, Encoding.UTF8.GetString(input), new UTF8Encoding(false));
                _ = app.TryNormalizeCuePrimaryBinReference(path, out _);
                Directory.Delete(directory, recursive: true);
                break;
            }
            case "sevenzip-list":
                _ = app.ParseSevenZipListEntries(Encoding.UTF8.GetString(input));
                break;
            default:
                throw new InvalidOperationException("Unknown managed fuzz target: " + target);
        }
    }

    private static ProcessRunResult ExecuteExternalFuzzCase(
        string target,
        string inputPath,
        string appDirectory,
        TimeSpan timeout)
    {
        if (target == "csokit-container")
        {
            string executable = Path.Combine(appDirectory, "Tools", "hakamiq-cso", "win-x64", "csokit.exe");
            return RunBoundedProcess(executable, ["info", inputPath, "--json"], timeout);
        }

        string sevenZip = Path.Combine(appDirectory, "Tools", "7zip", "7z.exe");
        return RunBoundedProcess(sevenZip, ["l", "-slt", "-ba", "--", inputPath], timeout);
    }

    private static int RunSoakCampaign(
        string[] args,
        AppReflection app,
        string outputRoot)
    {
        int durationMinutes = ReadBoundedInt(args, "--duration-minutes", 60, 0, 10_080);
        int durationSeconds = ReadBoundedInt(args, "--duration-seconds", 0, 0, 86_400);
        int maximumIterations = ReadBoundedInt(args, "--iterations", int.MaxValue, 1, int.MaxValue);
        int sampleMebibytes = ReadBoundedInt(args, "--sample-mebibytes", 4, 1, 256);
        TimeSpan duration = durationSeconds > 0
            ? TimeSpan.FromSeconds(durationSeconds)
            : TimeSpan.FromMinutes(durationMinutes);

        if (duration <= TimeSpan.Zero && maximumIterations == int.MaxValue)
        {
            throw new ArgumentException("Soak mode requires a positive duration or an explicit iteration count.");
        }

        string campaignRoot = Path.Combine(outputRoot, "soak");
        string checkpointPath = Path.Combine(campaignRoot, "checkpoint.json");
        Directory.CreateDirectory(campaignRoot);

        string workRoot = Path.Combine(
            Path.GetTempPath(),
            "HakamiqChdTool.SecuritySoak",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workRoot);

        Stopwatch stopwatch = Stopwatch.StartNew();
        long baselinePrivateBytes = 0;
        int baselineHandles = 0;
        long peakPrivateBytes = 0;
        int peakHandles = 0;
        int completed = 0;

        try
        {
            while (completed < maximumIterations
                   && (duration <= TimeSpan.Zero || stopwatch.Elapsed < duration))
            {
                RunSingleCsoSoakIteration(app, workRoot, completed, sampleMebibytes);
                completed++;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                using Process current = Process.GetCurrentProcess();
                current.Refresh();
                long privateBytes = current.PrivateMemorySize64;
                int handleCount = current.HandleCount;

                if (completed == 1)
                {
                    baselinePrivateBytes = privateBytes;
                    baselineHandles = handleCount;
                }

                peakPrivateBytes = Math.Max(peakPrivateBytes, privateBytes);
                peakHandles = Math.Max(peakHandles, handleCount);

                WriteJsonAtomically(
                    checkpointPath,
                    new
                    {
                        format = "HakamiqSoakCheckpoint.v1",
                        updatedAtUtc = DateTimeOffset.UtcNow,
                        completedIterations = completed,
                        elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                        sampleMebibytes,
                        baselinePrivateBytes,
                        currentPrivateBytes = privateBytes,
                        peakPrivateBytes,
                        baselineHandles,
                        currentHandles = handleCount,
                        peakHandles
                    });

                if (privateBytes - baselinePrivateBytes > 512L * 1024L * 1024L)
                {
                    throw new InvalidOperationException("Soak private-memory growth exceeded the 512 MiB safety budget.");
                }

                if (handleCount - baselineHandles > 256)
                {
                    throw new InvalidOperationException("Soak handle growth exceeded the 256-handle safety budget.");
                }
            }

            stopwatch.Stop();
            WriteJsonAtomically(
                Path.Combine(campaignRoot, "soak-report.json"),
                new
                {
                    format = "HakamiqSoakReport.v1",
                    completedAtUtc = DateTimeOffset.UtcNow,
                    requestedDurationSeconds = duration.TotalSeconds,
                    elapsedSeconds = stopwatch.Elapsed.TotalSeconds,
                    completedIterations = completed,
                    sampleMebibytes,
                    baselinePrivateBytes,
                    peakPrivateBytes,
                    baselineHandles,
                    peakHandles,
                    result = "passed"
                });

            Console.WriteLine(
                $"[PASS] Soak campaign completed: iterations={completed}; elapsed={stopwatch.Elapsed}; peakPrivateBytes={peakPrivateBytes}; peakHandles={peakHandles}");
            return 0;
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private static void RunSingleCsoSoakIteration(
        AppReflection app,
        string workRoot,
        int iteration,
        int sampleMebibytes)
    {
        string iterationRoot = Path.Combine(workRoot, $"iteration-{iteration:D8}");
        Directory.CreateDirectory(iterationRoot);
        string inputPath = Path.Combine(iterationRoot, "input.iso");
        string csoPath = Path.Combine(iterationRoot, "output.cso");
        byte[] sample = new byte[checked(sampleMebibytes * 1024 * 1024)];
        var random = new Random(unchecked(0x48435400 + iteration));
        random.NextBytes(sample);

        // Mix incompressible and repeated regions so every pass exercises raw and compressed blocks.
        for (int offset = 0; offset + 4096 <= sample.Length; offset += 64 * 1024)
        {
            Array.Fill(sample, (byte)(iteration % 251), offset, 4096);
        }

        File.WriteAllBytes(inputPath, sample);
        object compression = app.RunBundledCsoKit(
            [
                "compress", inputPath,
                "-o", csoPath,
                "--profile", "game-safe",
                "--threads", "1",
                "--block", "2048",
                "--deep-verify",
                "--json"
            ]);

        AssertEqual(0, GetInt(compression, "ExitCode"), "CsoKit soak compression failed.");
        using IDisposable workspace = app.CreateCsoTempWorkspace(out string preparedIsoPath);
        object preparation = app.PreprocessCso(csoPath, preparedIsoPath);
        AssertTrue(GetBool(preparation, "IsSuccess"), "Application CsoKit soak preprocessing failed.");

        byte[] inputHash = SHA256.HashData(File.ReadAllBytes(inputPath));
        byte[] outputHash = SHA256.HashData(File.ReadAllBytes(preparedIsoPath));
        AssertTrue(
            CryptographicOperations.FixedTimeEquals(inputHash, outputHash),
            "CsoKit soak round trip changed the ISO payload.");

        Directory.Delete(iterationRoot, recursive: true);
    }

    private static int RunCorpusCampaign(
        string[] args,
        AppReflection app,
        string appDirectory,
        string outputRoot)
    {
        string? requestedRoot = ReadOptionalArgument(args, "--corpus-root");
        int maximumFiles = ReadBoundedInt(args, "--max-files", 2_000, 1, 100_000);
        long maximumBytes = ReadBoundedLong(args, "--max-total-bytes", 2L * 1024L * 1024L * 1024L * 1024L, 1, long.MaxValue);
        int timeoutSeconds = ReadBoundedInt(args, "--per-file-timeout-seconds", 600, 5, 7_200);

        string? syntheticRoot = null;
        string corpusRoot;
        object? runtimeTools = null;
        string chdmanPath;

        try
        {
            runtimeTools = app.CreateRuntimeToolService();
            chdmanPath = app.GetRuntimeChdmanPath(runtimeTools);

            if (string.IsNullOrWhiteSpace(requestedRoot))
            {
                syntheticRoot = Path.Combine(
                    Path.GetTempPath(),
                    "HakamiqChdTool.SecurityCorpus",
                    Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(syntheticRoot);
                BuildSyntheticCorpus(appDirectory, chdmanPath, syntheticRoot);
                corpusRoot = syntheticRoot;
            }
            else
            {
                corpusRoot = Path.GetFullPath(requestedRoot);
                if (!Directory.Exists(corpusRoot))
                {
                    throw new DirectoryNotFoundException("Corpus root was not found: " + corpusRoot);
                }
            }

            string[] supportedExtensions = [".chd", ".cso", ".zso", ".dax", ".iso", ".cue", ".bin", ".zip", ".7z", ".rar"];
            var corpusDirectory = new DirectoryInfo(corpusRoot);
            if ((corpusDirectory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException("Corpus root must not be a reparse point.");
            }

            var enumerationOptions = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                ReturnSpecialDirectories = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
                MaxRecursionDepth = 32
            };
            FileInfo[] files = new DirectoryInfo(corpusRoot)
                .EnumerateFiles("*", enumerationOptions)
                .Where(file => supportedExtensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase))
                .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase)
                .Take(maximumFiles + 1)
                .ToArray();

            if (files.Length > maximumFiles)
            {
                throw new InvalidOperationException($"Corpus file count exceeds the configured limit of {maximumFiles}.");
            }

            long totalBytes = files.Aggregate(0L, (total, file) => checked(total + file.Length));
            if (totalBytes > maximumBytes)
            {
                throw new InvalidOperationException($"Corpus size exceeds the configured limit of {maximumBytes} bytes.");
            }

            var results = new List<object>(files.Length);
            TimeSpan timeout = TimeSpan.FromSeconds(timeoutSeconds);

            foreach (FileInfo file in files)
            {
                string relativePath = Path.GetRelativePath(corpusRoot, file.FullName).Replace('\\', '/');
                string sha256 = ComputeSha256File(file.FullName, timeout);
                ProcessRunResult? verification = null;
                string classification = "not-applicable";

                if (file.Extension.Equals(".chd", StringComparison.OrdinalIgnoreCase))
                {
                    classification = GetEnumName(app.ClassifyMediaInput(file.FullName), "ProbeStatus");
                    verification = RunBoundedProcess(chdmanPath, ["verify", "-i", file.FullName], timeout);
                }
                else if (file.Extension.Equals(".cso", StringComparison.OrdinalIgnoreCase)
                         || file.Extension.Equals(".zso", StringComparison.OrdinalIgnoreCase)
                         || file.Extension.Equals(".dax", StringComparison.OrdinalIgnoreCase))
                {
                    string csoKit = Path.Combine(appDirectory, "Tools", "hakamiq-cso", "win-x64", "csokit.exe");
                    verification = RunBoundedProcess(csoKit, ["verify", file.FullName, "--deep", "--sha256", "--json"], timeout);
                }
                else if (file.Extension.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                         || file.Extension.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                         || file.Extension.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    string sevenZip = Path.Combine(appDirectory, "Tools", "7zip", "7z.exe");
                    verification = RunBoundedProcess(sevenZip, ["l", "-slt", "-ba", "--", file.FullName], timeout);
                }
                else
                {
                    classification = GetEnumName(app.ClassifyMediaInput(file.FullName), "ProbeStatus");
                }

                if (verification is { TimedOut: true } || verification is { ExitCode: < 0 })
                {
                    throw new InvalidOperationException(
                        $"Corpus processor did not terminate safely for '{relativePath}'. ExitCode={verification?.ExitCode}; TimedOut={verification?.TimedOut}.");
                }

                results.Add(new
                {
                    relativePath,
                    file.Length,
                    sha256,
                    classification,
                    verifierExitCode = verification?.ExitCode,
                    verifierTimedOut = verification?.TimedOut,
                    verifierOutputTruncated = verification?.OutputTruncated
                });
            }

            string campaignRoot = Path.Combine(outputRoot, "corpus");
            Directory.CreateDirectory(campaignRoot);
            string reportPath = Path.Combine(campaignRoot, "corpus-report.json");
            WriteJsonAtomically(
                reportPath,
                new
                {
                    format = "HakamiqCorpusReport.v1",
                    completedAtUtc = DateTimeOffset.UtcNow,
                    corpusKind = syntheticRoot is null ? "external" : "synthetic",
                    fileCount = files.Length,
                    totalBytes,
                    limits = new { maximumFiles, maximumBytes, perFileTimeoutSeconds = timeoutSeconds },
                    files = results
                });

            Console.WriteLine(
                $"[PASS] Corpus campaign completed: files={files.Length}; bytes={totalBytes}; kind={(syntheticRoot is null ? "external" : "synthetic")}; report={reportPath}");
            return 0;
        }
        finally
        {
            if (runtimeTools is not null)
            {
                app.CleanupRuntimeToolSession(runtimeTools);
            }

            if (syntheticRoot is not null)
            {
                TryDeleteDirectory(syntheticRoot);
            }
        }
    }

    private static void BuildSyntheticCorpus(string appDirectory, string chdmanPath, string corpusRoot)
    {
        string isoPath = Path.Combine(corpusRoot, "synthetic.iso");
        byte[] iso = new byte[256 * 1024];
        for (int index = 0; index < iso.Length; index++)
        {
            iso[index] = (byte)((index * 29) % 251);
        }
        File.WriteAllBytes(isoPath, iso);

        string csoKit = Path.Combine(appDirectory, "Tools", "hakamiq-cso", "win-x64", "csokit.exe");
        string csoPath = Path.Combine(corpusRoot, "valid.cso");
        ProcessRunResult csoResult = RunBoundedProcess(
            csoKit,
            ["compress", isoPath, "-o", csoPath, "--profile", "game-safe", "--threads", "1", "--block", "2048", "--deep-verify", "--json"],
            TimeSpan.FromMinutes(2));
        if (csoResult.ExitCode != 0 || csoResult.TimedOut)
        {
            throw new InvalidOperationException("Unable to generate the synthetic CSO corpus seed.");
        }

        byte[] validCso = File.ReadAllBytes(csoPath);
        File.WriteAllBytes(Path.Combine(corpusRoot, "truncated.cso"), validCso[..Math.Min(31, validCso.Length)]);
        byte[] corruptedCso = (byte[])validCso.Clone();
        corruptedCso[Math.Min(corruptedCso.Length - 1, 40)] ^= 0xA5;
        File.WriteAllBytes(Path.Combine(corpusRoot, "corrupted-index.cso"), corruptedCso);

        string chdPath = Path.Combine(corpusRoot, "valid.chd");
        ProcessRunResult chdResult = RunBoundedProcess(
            chdmanPath,
            ["createdvd", "-i", isoPath, "-o", chdPath],
            TimeSpan.FromMinutes(2));
        if (chdResult.ExitCode == 0 && File.Exists(chdPath))
        {
            byte[] validChd = File.ReadAllBytes(chdPath);
            File.WriteAllBytes(Path.Combine(corpusRoot, "truncated.chd"), validChd[..Math.Min(80, validChd.Length)]);
            byte[] corruptedChd = (byte[])validChd.Clone();
            corruptedChd[Math.Min(corruptedChd.Length - 1, 16)] ^= 0x5A;
            File.WriteAllBytes(Path.Combine(corpusRoot, "corrupted-header.chd"), corruptedChd);
        }
        else
        {
            File.WriteAllBytes(Path.Combine(corpusRoot, "malformed.chd"), GetManagedFuzzSeed("chd-header"));
        }
    }

    private static byte[] GetManagedFuzzSeed(string target)
    {
        if (target == "cue-descriptor")
        {
            return Encoding.UTF8.GetBytes(
                "FILE \"track.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00\r\n");
        }

        if (target == "sevenzip-list")
        {
            return Encoding.UTF8.GetBytes(
                "Path = game.iso\r\nSize = 1048576\r\nPacked Size = 524288\r\nFolder = -\r\n\r\n");
        }

        byte[] header = new byte[124];
        Encoding.ASCII.GetBytes("MComprHD").CopyTo(header, 0);
        WriteBigEndianInt32(header, 8, 124);
        WriteBigEndianInt32(header, 12, 5);
        return header;
    }

    private static byte[] GetExternalFuzzSeed(string target)
    {
        if (target == "sevenzip-archive")
        {
            return [0x50, 0x4B, 0x03, 0x04, 20, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        }

        byte[] header = new byte[64];
        Encoding.ASCII.GetBytes("CISO").CopyTo(header, 0);
        header[4] = 24;
        header[16] = 0;
        header[17] = 8;
        return header;
    }

    private static byte[] CreateMutatedInput(byte[] seed, Random random)
    {
        var bytes = new List<byte>(seed);
        int mutationCount = random.Next(1, 9);

        for (int mutation = 0; mutation < mutationCount; mutation++)
        {
            switch (random.Next(6))
            {
                case 0 when bytes.Count > 0:
                    bytes[random.Next(bytes.Count)] ^= (byte)random.Next(1, 256);
                    break;
                case 1 when bytes.Count > 0:
                {
                    int removeAt = random.Next(bytes.Count);
                    int maximumRemoval = Math.Min(32, bytes.Count - removeAt);
                    bytes.RemoveRange(removeAt, random.Next(1, maximumRemoval + 1));
                    break;
                }
                case 2 when bytes.Count < MaximumFuzzInputBytes:
                {
                    int insertAt = random.Next(bytes.Count + 1);
                    int length = random.Next(1, Math.Min(64, MaximumFuzzInputBytes - bytes.Count) + 1);
                    byte[] insertion = new byte[length];
                    random.NextBytes(insertion);
                    bytes.InsertRange(insertAt, insertion);
                    break;
                }
                case 3 when bytes.Count > 1:
                {
                    int newLength = random.Next(bytes.Count);
                    bytes.RemoveRange(newLength, bytes.Count - newLength);
                    break;
                }
                case 4 when bytes.Count > 0 && bytes.Count < MaximumFuzzInputBytes:
                {
                    int source = random.Next(bytes.Count);
                    int length = Math.Min(random.Next(1, 65), bytes.Count - source);
                    byte[] copy = bytes.Skip(source).Take(length).ToArray();
                    int room = MaximumFuzzInputBytes - bytes.Count;
                    bytes.InsertRange(random.Next(bytes.Count + 1), copy.Take(room));
                    break;
                }
                case 5 when bytes.Count > 0:
                    bytes[random.Next(bytes.Count)] = (byte)(random.Next(2) == 0 ? 0 : 0xFF);
                    break;
                default:
                    break;
            }
        }

        return bytes.Take(MaximumFuzzInputBytes).ToArray();
    }

    private static void PreserveFuzzFailure(
        string activePath,
        string crashRoot,
        string target,
        int caseIndex,
        int seed,
        Exception exception)
    {
        string stem = $"{target}-seed-{seed}-case-{caseIndex:D8}";
        string crashPath = Path.Combine(crashRoot, stem + ".bin");
        File.Copy(activePath, crashPath, overwrite: true);
        WriteJsonAtomically(
            Path.Combine(crashRoot, stem + ".json"),
            new
            {
                format = "HakamiqFuzzCrash.v1",
                target,
                seed,
                caseIndex,
                capturedAtUtc = DateTimeOffset.UtcNow,
                inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(crashPath))),
                exceptionType = exception.GetType().FullName,
                exception.Message,
                exception.StackTrace
            });
    }

    private static ProcessRunResult RunBoundedProcess(
        string executablePath,
        IReadOnlyList<string> arguments,
        TimeSpan timeout)
    {
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException("Security campaign tool was not found.", executablePath);
        }

        var startInfo = new ProcessStartInfo(executablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException("Unable to start security campaign tool: " + executablePath);
        }

        const int maximumCapturedCharacters = 256 * 1024;
        var output = new StringBuilder();
        var error = new StringBuilder();
        var captureSync = new object();
        bool outputLimitExceeded = false;

        void CaptureLine(StringBuilder destination, string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (captureSync)
            {
                if (outputLimitExceeded)
                {
                    return;
                }

                if (output.Length + error.Length + line.Length + Environment.NewLine.Length > maximumCapturedCharacters)
                {
                    outputLimitExceeded = true;
                    TryKillProcessTree(process);
                    return;
                }

                destination.AppendLine(line);
            }
        }

        process.OutputDataReceived += (_, eventArgs) => CaptureLine(output, eventArgs.Data);
        process.ErrorDataReceived += (_, eventArgs) => CaptureLine(error, eventArgs.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        bool timedOut = !process.WaitForExit((int)Math.Min(int.MaxValue, timeout.TotalMilliseconds));

        if (timedOut)
        {
            TryKillProcessTree(process);
            if (!process.WaitForExit(5_000))
            {
                throw new TimeoutException(
                    "Security campaign tool remained alive after process-tree termination was requested.");
            }
        }

        // The process has exited here; the parameterless wait flushes async output callbacks.
        process.WaitForExit();
        string capturedOutput;
        string capturedError;
        lock (captureSync)
        {
            capturedOutput = output.ToString();
            capturedError = error.ToString();
        }

        return new ProcessRunResult(
            timedOut ? -2 : process.ExitCode,
            timedOut,
            outputLimitExceeded,
            capturedOutput,
            capturedError);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (Win32Exception)
        {
        }
    }

    private static string ComputeSha256File(string path, TimeSpan timeout)
    {
        using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 1024,
            FileOptions.SequentialScan);
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[1024 * 1024];
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            if (stopwatch.Elapsed > timeout)
            {
                throw new TimeoutException("Corpus hashing exceeded the per-file timeout: " + path);
            }

            int bytesRead = stream.Read(buffer, 0, buffer.Length);
            if (bytesRead == 0)
            {
                break;
            }

            hash.AppendData(buffer, 0, bytesRead);
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static int ReadBoundedInt(string[] args, string name, int defaultValue, int minimum, int maximum)
    {
        string? value = ReadOptionalArgument(args, name);
        if (value is null)
        {
            return defaultValue;
        }

        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new ArgumentOutOfRangeException(name, value, $"Expected an integer from {minimum} to {maximum}.");
        }

        return parsed;
    }

    private static long ReadBoundedLong(string[] args, string name, long defaultValue, long minimum, long maximum)
    {
        string? value = ReadOptionalArgument(args, name);
        if (value is null)
        {
            return defaultValue;
        }

        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed)
            || parsed < minimum
            || parsed > maximum)
        {
            throw new ArgumentOutOfRangeException(name, value, $"Expected an integer from {minimum} to {maximum}.");
        }

        return parsed;
    }

    private static void WriteJsonAtomically(string path, object value)
    {
        string fullPath = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temporaryPath = fullPath + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(value, SecurityJsonOptions), new UTF8Encoding(false));
        File.Move(temporaryPath, fullPath, overwrite: true);
    }

    private static void WriteBigEndianInt32(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed record ProcessRunResult(
        int ExitCode,
        bool TimedOut,
        bool OutputTruncated,
        string StandardOutput,
        string StandardError);
}
