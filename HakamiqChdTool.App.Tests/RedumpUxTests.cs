using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.QueueRun;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private static void TestRedumpIsoAndChdPaths(AppReflection app, string workDirectory)
    {
        TestRedumpIsoAndChdPathsAsync(app, workDirectory).GetAwaiter().GetResult();
    }

    private static async Task TestRedumpIsoAndChdPathsAsync(AppReflection app, string workDirectory)
    {
        string root = Path.Combine(workDirectory, "redump-ux-integration");
        Directory.CreateDirectory(root);

        string isoPath = Path.Combine(root, "source.iso");
        byte[] isoBytes = new byte[2048 * 512];
        for (int index = 0; index < isoBytes.Length; index++)
        {
            isoBytes[index] = (byte)((index * 31) % 251);
        }

        await File.WriteAllBytesAsync(isoPath, isoBytes).ConfigureAwait(false);

        string md5 = Convert.ToHexString(MD5.HashData(isoBytes)).ToLowerInvariant();
        string sha1 = Convert.ToHexString(SHA1.HashData(isoBytes)).ToLowerInvariant();
        string crc = Crc32Hex(isoBytes);
        string datPath = Path.Combine(root, "matching.dat");

        // A real system is required: a match against an unmapped system fails closed.
        await File.WriteAllTextAsync(
                datPath,
                $"<datafile><header><name>Sony - PlayStation Portable - Datfile</name></header><game name=\"Test Game\"><description>Test Game</description><rom name=\"source.iso\" size=\"{isoBytes.Length}\" crc=\"{crc}\" md5=\"{md5}\" sha1=\"{sha1}\" /></game></datafile>",
                Encoding.UTF8)
            .ConfigureAwait(false);

        object databaseObject = app.CreateRedumpDatabase(Path.Combine(root, "redump.db"));
        object importResult = app.CleanRebuildRedumpDatabase(databaseObject, [datPath]);
        AssertTrue(GetBool(importResult, "Success"), "Redump integration DAT import failed.");
        var database = (RedumpSqliteManager)databaseObject;

        object runtimeTools = app.CreateRuntimeToolService();
        string chdmanPath = app.GetRuntimeChdmanPath(runtimeTools);

        var directProgress = new ConcurrentQueue<ProgressEvent>();
        DeepHashAnalysisResult directResult = await ScanWithOuterLeaseAsync(
                isoPath,
                database,
                chdmanPath,
                directProgress)
            .ConfigureAwait(false);

        AssertEqual(IntegrityValidationState.Verified, directResult.State, "Direct ISO should match Redump.");
        AssertEqual(RedumpVerification.Full, directResult.RedumpVerification, "A direct PSP ISO match is a full Redump proof.");
        AssertEqual(MediaSemantics.SingleRawImage2048, directResult.MediaSemantics, "PSP ISO semantics.");
        AssertEqual(StorageCompatibility.Native, directResult.StorageCompatibility, "PSP ISO storage compatibility.");
        AssertTrue(
            directProgress.Any(static item => item.MessageKey == "LocRedumpV2_StepHash"),
            "Direct ISO should report the disc fingerprint stage.");
        AssertFalse(
            directProgress.Any(static item => item.MessageKey == "LocRedumpV2_NormalizeChd"),
            "Direct ISO must not report CHD reading.");

        string chdPath = Path.Combine(root, "source.chd");
        ChdmanCliRunner.Result createResult = await ChdmanCliRunner.ExecuteAsync(
                chdmanPath,
                ["createdvd", "-i", isoPath, "-o", chdPath, "-f"],
                parseProgressPercent: true,
                progress: null,
                onProcessStarted: null,
                cancellationToken: CancellationToken.None,
                exclusiveFileAccessPath: isoPath)
            .ConfigureAwait(false);

        AssertEqual(0, createResult.ExitCode, "Synthetic CHD creation failed: " + createResult.StandardError);
        AssertTrue(File.Exists(chdPath), "Synthetic CHD was not created.");

        var chdProgress = new ConcurrentQueue<ProgressEvent>();
        DeepHashAnalysisResult chdResult = await ScanWithOuterLeaseAsync(
                chdPath,
                database,
                chdmanPath,
                chdProgress)
            .ConfigureAwait(false);

        AssertEqual(
            IntegrityValidationState.Verified,
            chdResult.State,
            "Normalized CHD should match Redump. " + DescribeRedumpResult(chdResult));
        AssertEqual(
            "LocDeepHash_StatusVerifiedNormalized",
            chdResult.StatusMessageKey,
            "CHD match should be identified as temporary normalization.");
        AssertEqual(RedumpVerification.Full, chdResult.RedumpVerification, "extractdvd + all hashes is a full Redump proof.");
        AssertEqual(StorageCompatibility.Supported, chdResult.StorageCompatibility, "PPSSPP supports CHD.");
        AssertTrue(
            chdProgress.Any(static item => item.MessageKey == "LocRedumpV2_NormalizeChd"),
            "CHD should report that disc data is being read from the CHD file.");
        AssertTrue(
            chdProgress.Any(static item => item.MessageKey == "LocRedumpV2_StepHash"),
            "CHD should report the disc fingerprint stage after reading its data.");
        AssertTrue(
            chdProgress.Any(static item => item.MessageKey == "LocRedumpV2_StepMatch"),
            "CHD should report the Redump matching stage.");

        string noMatchIsoPath = Path.Combine(root, "no-match.iso");
        byte[] noMatchBytes = (byte[])isoBytes.Clone();
        noMatchBytes[^1] ^= 0x5A;
        await File.WriteAllBytesAsync(noMatchIsoPath, noMatchBytes).ConfigureAwait(false);

        DeepHashAnalysisResult noMatchResult = await ScanWithOuterLeaseAsync(
                noMatchIsoPath,
                database,
                chdmanPath,
                new ConcurrentQueue<ProgressEvent>())
            .ConfigureAwait(false);

        AssertEqual(
            IntegrityValidationState.NoRedumpMatch,
            noMatchResult.State,
            "A readable source with different hashes should report no Redump match.");

        string invalidChdPath = Path.Combine(root, "invalid.chd");
        await File.WriteAllBytesAsync(invalidChdPath, Encoding.ASCII.GetBytes("MComprHD-invalid"))
            .ConfigureAwait(false);

        DeepHashAnalysisResult invalidResult = await ScanWithOuterLeaseAsync(
                invalidChdPath,
                database,
                chdmanPath,
                new ConcurrentQueue<ProgressEvent>())
            .ConfigureAwait(false);

        AssertEqual(
            IntegrityValidationState.Failed,
            invalidResult.State,
            "An invalid CHD should finish with an explicit failure instead of waiting indefinitely.");
    }

    private static void TestRedumpEngineBoundsDescriptorSize(AppReflection app, string workDirectory)
    {
        string root = Path.Combine(workDirectory, "redump-descriptor-bounds");
        Directory.CreateDirectory(root);

        byte[] track = [1, 2, 3, 4];
        string datPath = Path.Combine(root, "d.dat");
        File.WriteAllText(
            datPath,
            $"<datafile><header><name>Sony - PlayStation - Datfile</name></header><game name=\"G\"><description>G</description><rom name=\"a.bin\" size=\"4\" crc=\"{Crc32Hex(track)}\" md5=\"{Convert.ToHexString(MD5.HashData(track)).ToLowerInvariant()}\" sha1=\"{Convert.ToHexString(SHA1.HashData(track)).ToLowerInvariant()}\" /></game></datafile>",
            Encoding.UTF8);

        object databaseObject = app.CreateRedumpDatabase(Path.Combine(root, "redump.db"));
        AssertTrue(GetBool(app.CleanRebuildRedumpDatabase(databaseObject, [datPath]), "Success"), "Descriptor-bounds DAT import failed.");

        File.WriteAllBytes(Path.Combine(root, "a.bin"), track);
        byte[] oversized = new byte[5 * 1024 * 1024];
        Array.Fill(oversized, (byte)' ');
        Encoding.ASCII.GetBytes("FILE \"a.bin\" BINARY\r\n").CopyTo(oversized, 0);
        string cuePath = Path.Combine(root, "huge.cue");
        File.WriteAllBytes(cuePath, oversized);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        DeepHashAnalysisResult result = DeepHashAnalyzer.DeepHashAnalyzeAsync(
                cuePath,
                (RedumpSqliteManager)databaseObject,
                timeout.Token,
                new RedumpV2ScanOptions(string.Empty, Settings: null, SourcePathLeaseHeld: false))
            .GetAwaiter()
            .GetResult();

        AssertTrue(result.IsFatalInputReadFailure, "A descriptor over the size bound is refused as an input failure, never read whole. " + DescribeRedumpResult(result));
        AssertTrue(result.State != IntegrityValidationState.Verified, "An oversized descriptor can never verify.");
    }

    private static async Task<DeepHashAnalysisResult> ScanWithOuterLeaseAsync(
        string sourcePath,
        RedumpSqliteManager database,
        string chdmanPath,
        ConcurrentQueue<ProgressEvent> events,
        IFastPathProofRegistry? fastPathProof = null)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await using IAsyncDisposable lease = await FilePathExclusiveGate
            .AcquireAsync(sourcePath, timeout.Token)
            .ConfigureAwait(false);

        return await DeepHashAnalyzer.DeepHashAnalyzeAsync(
                sourcePath,
                database,
                timeout.Token,
                new RedumpV2ScanOptions(
                    chdmanPath,
                    Settings: null,
                    SourcePathLeaseHeld: true,
                    FastPathProof: fastPathProof),
                new InlineProgress<ProgressEvent>(events.Enqueue))
            .ConfigureAwait(false);
    }

    private static string DescribeRedumpResult(DeepHashAnalysisResult result)
    {
        return $"StatusKey={result.StatusMessageKey}; DetailKey={result.DetailTooltipKey}; "
            + $"FailureCode={result.FailureCode}; HashedFiles={result.HashedFileCount}; "
            + $"Matches={result.MatchedFileCount}.";
    }

    private static void TestRedumpCommandsHonorEnabledState()
    {
        IMainWindowSession session = DispatchProxy.Create<IMainWindowSession, DefaultValueDispatchProxy>();
        IQueueRunCoordinator coordinator = DispatchProxy.Create<IQueueRunCoordinator, DefaultValueDispatchProxy>();

        using var viewModel = new MainWindowViewModel(session, coordinator, new ArrayList());

        viewModel.IsRedumpFeatureVisible = false;
        AssertFalse(
            viewModel.VerifyAllRedumpToolbarCommand.CanExecute(null),
            "Redump commands must be disabled when the feature is disabled.");

        viewModel.IsRedumpFeatureVisible = true;
        AssertTrue(
            viewModel.VerifyAllRedumpToolbarCommand.CanExecute(null),
            "Redump commands should be enabled when the feature is enabled and a scan is available.");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        private readonly Action<T> _report = report ?? throw new ArgumentNullException(nameof(report));

        public void Report(T value) => _report(value);
    }

    private class DefaultValueDispatchProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name.StartsWith("CanRunRedumpIntegrity", StringComparison.Ordinal))
            {
                return true;
            }

            Type returnType = targetMethod.ReturnType;
            if (returnType == typeof(void))
            {
                return null;
            }

            if (returnType == typeof(Task))
            {
                return Task.CompletedTask;
            }

            if (returnType == typeof(bool))
            {
                return false;
            }

            if (returnType.IsValueType)
            {
                return Activator.CreateInstance(returnType);
            }

            return null;
        }
    }
}
