using HakamiqChdTool.App.Core.Verification;
using HakamiqChdTool.App.Models.Chd;

#pragma warning disable IDE0130 // Namespace is intentionally kept stable for existing public API and call sites.
namespace HakamiqChdTool.App.Services;

// Media-semantics policy for Redump verification: which proof a result may claim, kept
// separate from emulator/storage compatibility and from chdman integrity checks.
public sealed partial class RedumpV2Engine
{
    private const string SpecializedPlatformKey = "LocRedumpV2_SpecializedPlatform";
    private const string MediaSystemMismatchKey = "LocRedumpV2_MediaSystemMismatch";
    private const string ProofIncompleteKey = "LocRedumpV2_ProofIncomplete";
    private const string StatusVerifiedFastPathKey = "LocRedumpV2_StatusVerifiedFastPath";
    private const string DetailFastPathKey = "LocRedumpV2_DetailFastPath";

    private static DiscContainer ContainerFor(RedumpSourceFormat format) =>
        format switch
        {
            RedumpSourceFormat.Iso or RedumpSourceFormat.DecryptedPs3Iso => DiscContainer.Iso,
            RedumpSourceFormat.Gcm => DiscContainer.Gcm,
            RedumpSourceFormat.BinCue => DiscContainer.CueBin,
            RedumpSourceFormat.Gdi => DiscContainer.Gdi,
            RedumpSourceFormat.Chd => DiscContainer.Chd,
            RedumpSourceFormat.Cso => DiscContainer.Cso,
            RedumpSourceFormat.Rvz or RedumpSourceFormat.Wbfs or RedumpSourceFormat.Nkit => DiscContainer.DolphinCompressed,
            RedumpSourceFormat.Ps3JbFolder => DiscContainer.Ps3Folder,
            RedumpSourceFormat.Archive => DiscContainer.Archive,
            _ => DiscContainer.Unknown
        };

    // Structure known from the source format alone (CHD needs its metadata tags).
    private static DiscStructure StructureFor(RedumpSourceFormat format) =>
        format switch
        {
            RedumpSourceFormat.Iso or RedumpSourceFormat.Cso => DiscStructure.SingleImage2048,
            RedumpSourceFormat.Gcm => DiscStructure.NintendoOpticalImage,
            RedumpSourceFormat.BinCue => DiscStructure.CdTracks,
            RedumpSourceFormat.Gdi => DiscStructure.GdRomTracks,
            RedumpSourceFormat.Rvz or RedumpSourceFormat.Wbfs or RedumpSourceFormat.Nkit => DiscStructure.DolphinCompressedImage,
            RedumpSourceFormat.DecryptedPs3Iso => DiscStructure.Ps3DecryptedImage,
            RedumpSourceFormat.Ps3JbFolder => DiscStructure.Ps3DecryptedFolder,
            _ => DiscStructure.Unknown
        };

    private static DiscVerificationPolicy ProvisionalPolicy(
        RedumpSourceFormat format,
        RedumpV2ScanOptions? options = null) =>
        DiscPolicy.Resolve(StructureFor(format), ContainerFor(format), DiscSystem.Unknown, options?.FastPathProof);

    private static DiscVerificationPolicy ProvisionalPolicy(
        NormalizationOutcome normalization,
        RedumpV2ScanOptions? options = null) =>
        DiscPolicy.Resolve(normalization.Structure, normalization.Container, DiscSystem.Unknown, options?.FastPathProof);

    // Fast path for a DVD-style CHD: its raw data SHA-1 and logical size identify the whole
    // Redump image without extraction, but only for a system whose fast path has been
    // proven on a real corpus and only when the Redump hit is unique.
    private static FastPathHit? TryFastPath(
        ChdInfoResult info,
        RedumpSqliteManager? redumpDatabase,
        RedumpV2ScanOptions options)
    {
        IFastPathProofRegistry registry = options.FastPathProof ?? FastPathProofRegistry.Production;

        if (redumpDatabase is null
            || info.MetadataStructure != DiscStructure.SingleImage2048
            || string.IsNullOrWhiteSpace(info.DataSha1)
            || info.LogicalBytes is not > 0)
        {
            return null;
        }

        var hits = redumpDatabase.FindRomsBySha1AndSize(info.DataSha1, info.LogicalBytes.Value);
        if (hits.Count != 1)
        {
            return null;
        }

        (string systemName, string gameName, RedumpReferenceRom rom) = hits[0];

        DiscVerificationPolicy policy = DiscPolicy.Resolve(
            DiscStructure.SingleImage2048,
            DiscContainer.Chd,
            DiscPolicy.ResolveSystem(systemName),
            registry);

        return DiscPolicy.AssessFastPath(policy, info.DataSha1, info.LogicalBytes, [rom])
               == RedumpVerification.RawSha1AndSize
            ? new FastPathHit(systemName, gameName, rom, policy)
            : null;
    }

    private static RedumpV2ScanResult BuildFastPathResult(
        string originalPath,
        RedumpSourceFormat sourceFormat,
        FastPathHit hit)
    {
        var match = new DeepHashMatch(
            originalPath,
            hit.Rom.SizeBytes ?? 0,
            hit.Rom.Md5 ?? string.Empty,
            hit.Rom.Sha1 ?? string.Empty,
            hit.Rom.Crc32 ?? string.Empty,
            hit.SystemName,
            hit.GameName,
            hit.Rom.RomName,
            "RawSha1+Size",
            hit.Rom.Crc32 ?? string.Empty,
            string.Empty,
            string.Empty);

        return BuildTerminalResult(
            RedumpV2ResultState.Verified,
            originalPath,
            sourceFormat,
            RedumpNormalizedFormat.Original,
            usedTemporaryNormalization: false,
            requiredTempSpaceBytes: 0,
            StatusVerifiedFastPathKey,
            DetailFastPathKey,
            matches: [match],
            suggestedStandardName: BuildSuggestedStandardFileName(hit.Rom.RomName, originalPath),
            matchedSystemName: hit.SystemName,
            matchedGameName: hit.GameName,
            matchedFileCount: 1,
            hashedFileCount: 0,
            policy: hit.Policy,
            proof: RedumpVerification.RawSha1AndSize);
    }

    // A complete, single-disc hash match becomes a Redump claim only if the matched system
    // fits the disc's structure and every data ROM of that disc agrees on all hashes.
    private static RedumpV2ScanResult? EnforcePolicy(
        string originalPath,
        RedumpSourceFormat sourceFormat,
        NormalizationOutcome normalization,
        IReadOnlyList<DeepHashFileDigest> hashed,
        IReadOnlyList<DeepHashMatch> matches,
        RedumpSqliteManager redumpDatabase,
        RedumpV2ScanOptions options,
        out DiscVerificationPolicy policy,
        out RedumpVerification proof)
    {
        DeepHashMatch first = matches[0];
        DiscSystem system = DiscPolicy.ResolveSystem(first.SystemName);

        policy = DiscPolicy.Resolve(normalization.Structure, normalization.Container, system, options.FastPathProof);
        proof = RedumpVerification.NotVerified;

        // After a match the system must be known: an unmapped system is never assumed to be
        // equivalent to any known one.
        if (system == DiscSystem.Unknown || policy.Semantics == MediaSemantics.Unknown)
        {
            bool contradiction = system != DiscSystem.Unknown
                && system is not DiscSystem.Xbox and not DiscSystem.Xbox360;

            return BuildTerminalResult(
                contradiction ? RedumpV2ResultState.Failed : RedumpV2ResultState.Unsupported,
                originalPath,
                sourceFormat,
                normalization.Format,
                normalization.UsedTemporaryNormalization,
                normalization.RequiredTempSpaceBytes,
                contradiction ? "LocDeepHash_StatusConflictingMatch" : StatusUnsupportedKey,
                contradiction ? MediaSystemMismatchKey : SpecializedPlatformKey,
                [first.SystemName, normalization.Structure.ToString()],
                hashed,
                matches,
                policy: policy);
        }

        IReadOnlyList<RedumpReferenceRom> discRoms = redumpDatabase.GetDiscRoms(first.SystemName, first.GameName);
        List<HashedDiscFile> files = [.. hashed.Select(static file => new HashedDiscFile(file.SizeBytes, file.Md5, file.Sha1, file.Crc32))];

        proof = DiscPolicy.AssessProof(policy, files, discRoms);
        if (proof != RedumpVerification.NotVerified)
        {
            return null;
        }

        int dataRomCount = discRoms.Count(static rom =>
            !rom.RomName.EndsWith(".cue", StringComparison.OrdinalIgnoreCase)
            && !rom.RomName.EndsWith(".gdi", StringComparison.OrdinalIgnoreCase));

        return BuildTerminalResult(
            RedumpV2ResultState.Failed,
            originalPath,
            sourceFormat,
            normalization.Format,
            normalization.UsedTemporaryNormalization,
            normalization.RequiredTempSpaceBytes,
            StatusIncompleteKey,
            ProofIncompleteKey,
            [matches.Count, dataRomCount],
            hashed,
            matches,
            policy: policy);
    }

    private sealed record FastPathHit(
        string SystemName,
        string GameName,
        RedumpReferenceRom Rom,
        DiscVerificationPolicy Policy);
}
