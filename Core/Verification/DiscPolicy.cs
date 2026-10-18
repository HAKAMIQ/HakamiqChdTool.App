using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HakamiqChdTool.App.Core.Verification;

// Registry of systems whose Raw-SHA1 + logical-size fast path has been proven against a
// real source -> CHD -> Redump corpus. Nothing is proven by assumption: a system is only
// added after the corpus proof (security-mode "fastpath-proof") passes and is reviewed.
public interface IFastPathProofRegistry
{
    bool IsProven(DiscSystem system);
}

public sealed class FastPathProofRegistry : IFastPathProofRegistry
{
    private readonly HashSet<DiscSystem> _proven;

    public FastPathProofRegistry(IEnumerable<DiscSystem> provenSystems)
    {
        ArgumentNullException.ThrowIfNull(provenSystems);
        _proven = [.. provenSystems];
    }

    // Production registry. Empty until a corpus proof has been run and reviewed.
    public static FastPathProofRegistry Production { get; } = new([]);

    public bool IsProven(DiscSystem system) => _proven.Contains(system);
}

public static class DiscPolicy
{
    // Systems that may use the Raw-SHA1 fast path once proven: single 2048-byte images
    // whose Redump entry is the whole image.
    private static readonly HashSet<DiscSystem> FastPathCandidates =
    [
        DiscSystem.PlayStation2,
        DiscSystem.PlayStationPortable
    ];

    private static readonly HashSet<DiscSystem> TrackCdSystems =
    [
        DiscSystem.PlayStation,
        DiscSystem.PlayStation2,
        DiscSystem.Saturn,
        DiscSystem.SegaCd,
        DiscSystem.PcEngineCd,
        DiscSystem.NeoGeoCd,
        DiscSystem.ThreeDo,
        DiscSystem.JaguarCd,
        DiscSystem.Dreamcast // MIL-CD
    ];

    // Canonical identifiers: Redump catalog keys and normalized Redump DAT system names
    // ("Sony - PlayStation 2" -> "sonyplaystation2"). Exact matches only, so e.g.
    // "PlayStation" never captures "PlayStation 2".
    private static readonly Dictionary<string, DiscSystem> SystemIds = BuildSystemIds();

    public static DiscSystem ResolveSystem(string? systemNameOrKey)
    {
        if (string.IsNullOrWhiteSpace(systemNameOrKey))
        {
            return DiscSystem.Unknown;
        }

        return SystemIds.TryGetValue(NormalizeSystemId(systemNameOrKey), out DiscSystem system)
            ? system
            : DiscSystem.Unknown;
    }

    // Structure decides the semantics; a known system may only confirm it. A structure the
    // system cannot have (e.g. a DVD image for PlayStation) is a contradiction -> Unknown.
    // With no system yet (before a Redump match) the structure alone gives a provisional result.
    public static MediaSemantics ClassifySemantics(DiscStructure structure, DiscSystem system)
    {
        MediaSemantics structural = structure switch
        {
            DiscStructure.CdTracks => MediaSemantics.TrackStructuredCd,
            DiscStructure.GdRomTracks => MediaSemantics.GdRomTrackLayout,
            DiscStructure.SingleImage2048 => MediaSemantics.SingleRawImage2048,
            DiscStructure.NintendoOpticalImage or DiscStructure.DolphinCompressedImage => MediaSemantics.DolphinOpticalImage,
            DiscStructure.Ps3DecryptedImage or DiscStructure.Ps3DecryptedFolder => MediaSemantics.Ps3DecryptedIso,
            _ => MediaSemantics.Unknown
        };

        if (system == DiscSystem.Unknown || structural == MediaSemantics.Unknown)
        {
            return structural;
        }

        return system switch
        {
            _ when structural == MediaSemantics.TrackStructuredCd && TrackCdSystems.Contains(system) => structural,

            DiscSystem.Dreamcast when structural == MediaSemantics.GdRomTrackLayout => structural,

            DiscSystem.PlayStation2 or DiscSystem.PlayStationPortable
                when structural == MediaSemantics.SingleRawImage2048 => structural,

            DiscSystem.GameCube or DiscSystem.Wii
                when structural is MediaSemantics.DolphinOpticalImage or MediaSemantics.SingleRawImage2048
                => MediaSemantics.DolphinOpticalImage,

            DiscSystem.PlayStation3 when structural == MediaSemantics.SingleRawImage2048 => MediaSemantics.Ps3EncryptedDisc,
            DiscSystem.PlayStation3 when structural == MediaSemantics.Ps3DecryptedIso => structural,

            // Xbox, Xbox 360 and every contradiction: no assumed equivalence.
            _ => MediaSemantics.Unknown
        };
    }

    public static DiscVerificationPolicy Resolve(
        DiscStructure structure,
        DiscContainer container,
        DiscSystem system,
        IFastPathProofRegistry? proofRegistry = null)
    {
        IFastPathProofRegistry registry = proofRegistry ?? FastPathProofRegistry.Production;
        MediaSemantics semantics = ClassifySemantics(structure, system);

        (RedumpFastPath fastPath, FastPathState fastPathState) = ResolveFastPath(semantics, container, system, registry);
        FullVerificationPath fullPath = ResolveFullPath(semantics, container);
        RedumpVerification maxProof = ResolveMaxProof(semantics, fullPath);

        // Fail closed: no Redump claim can be made for this media, whatever else it passes
        // (emulator compatibility or chdman integrity checks).
        bool failClosed = maxProof is RedumpVerification.NotVerified or RedumpVerification.NotApplicable;

        return new DiscVerificationPolicy(
            system,
            semantics,
            fastPath,
            fastPathState,
            fullPath,
            ResolveStorage(system, container, semantics),
            maxProof,
            failClosed);
    }

    // Decides the proof actually achieved from hashed files and the matched disc's Redump
    // entries. Every data ROM of the disc must be present exactly once and agree on size,
    // MD5, SHA-1 and CRC32; one weak hash is never enough.
    public static RedumpVerification AssessProof(
        DiscVerificationPolicy policy,
        IReadOnlyList<HashedDiscFile> hashedFiles,
        IReadOnlyList<RedumpReferenceRom> discRoms)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(hashedFiles);
        ArgumentNullException.ThrowIfNull(discRoms);

        if (policy.MaxProof is RedumpVerification.NotApplicable or RedumpVerification.NotVerified)
        {
            return policy.MaxProof;
        }

        List<RedumpReferenceRom> dataRoms = discRoms.Where(static rom => !IsDescriptor(rom.RomName)).ToList();

        if (dataRoms.Count == 0 || dataRoms.Count != hashedFiles.Count)
        {
            return RedumpVerification.NotVerified;
        }

        var unmatched = new List<HashedDiscFile>(hashedFiles);

        foreach (RedumpReferenceRom rom in dataRoms)
        {
            int index = unmatched.FindIndex(file => AgreesOnAllHashes(file, rom));
            if (index < 0)
            {
                return RedumpVerification.NotVerified;
            }

            unmatched.RemoveAt(index);
        }

        return policy.MaxProof;
    }

    // The fast path proves a whole-image Redump entry from the CHD's raw data SHA-1 and
    // logical size without extraction. It needs a proven system and a unique Redump hit.
    public static RedumpVerification AssessFastPath(
        DiscVerificationPolicy policy,
        string chdRawSha1,
        long? chdLogicalBytes,
        IReadOnlyList<RedumpReferenceRom> hitsForSha1AndSize)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(hitsForSha1AndSize);

        if (!policy.CanUseFastPath
            || string.IsNullOrWhiteSpace(chdRawSha1)
            || chdLogicalBytes is not > 0
            || hitsForSha1AndSize.Count != 1)
        {
            return RedumpVerification.NotVerified;
        }

        RedumpReferenceRom hit = hitsForSha1AndSize[0];

        return hit.SizeBytes == chdLogicalBytes
               && HashEquals(hit.Sha1, chdRawSha1)
               && !IsDescriptor(hit.RomName)
            ? RedumpVerification.RawSha1AndSize
            : RedumpVerification.NotVerified;
    }

    private static (RedumpFastPath, FastPathState) ResolveFastPath(
        MediaSemantics semantics,
        DiscContainer container,
        DiscSystem system,
        IFastPathProofRegistry registry)
    {
        if (semantics != MediaSemantics.SingleRawImage2048
            || container != DiscContainer.Chd
            || !FastPathCandidates.Contains(system))
        {
            return (RedumpFastPath.None, FastPathState.NotAllowed);
        }

        return (
            RedumpFastPath.RawSha1AndLogicalSize,
            registry.IsProven(system) ? FastPathState.Proven : FastPathState.CandidateUnproven);
    }

    private static FullVerificationPath ResolveFullPath(MediaSemantics semantics, DiscContainer container) =>
        (semantics, container) switch
        {
            (MediaSemantics.TrackStructuredCd, DiscContainer.CueBin) => FullVerificationPath.TrackFileHash,
            (MediaSemantics.TrackStructuredCd, DiscContainer.Chd) => FullVerificationPath.ExtractCdTracks,

            (MediaSemantics.GdRomTrackLayout, DiscContainer.Gdi) => FullVerificationPath.TrackFileHash,
            (MediaSemantics.GdRomTrackLayout, DiscContainer.Chd) => FullVerificationPath.ExtractGdRomTracks,

            (MediaSemantics.SingleRawImage2048, DiscContainer.Iso) => FullVerificationPath.DirectImageHash,
            (MediaSemantics.SingleRawImage2048, DiscContainer.Chd) => FullVerificationPath.ExtractDvdImage,
            (MediaSemantics.SingleRawImage2048, DiscContainer.Cso) => FullVerificationPath.DecompressCsoImage,

            // GameCube/Wii: only the exact ISO/GCM source is hashed. RVZ/WIA/WBFS/NKit need a
            // Dolphin-native workflow; CHD is not a GameCube/Wii archival format.
            (MediaSemantics.DolphinOpticalImage, DiscContainer.Iso or DiscContainer.Gcm) => FullVerificationPath.DirectImageHash,
            (MediaSemantics.DolphinOpticalImage, DiscContainer.DolphinCompressed) => FullVerificationPath.PlatformNativeTool,

            (MediaSemantics.Ps3EncryptedDisc, DiscContainer.Iso) => FullVerificationPath.DirectImageHash,
            (MediaSemantics.Ps3DecryptedIso, DiscContainer.Ps3Folder) => FullVerificationPath.Ps3IrdFileCheck,

            // Nothing Redump-comparable, but a CHD can still be integrity-checked with chdman.
            (_, DiscContainer.Chd) => FullVerificationPath.IntegrityOnly,

            _ => FullVerificationPath.None
        };

    private static RedumpVerification ResolveMaxProof(
        MediaSemantics semantics,
        FullVerificationPath fullPath)
    {
        if (semantics == MediaSemantics.Ps3DecryptedIso)
        {
            // A decrypted image or folder is never the Redump original, whatever it plays in.
            return RedumpVerification.NotApplicable;
        }

        if (fullPath is FullVerificationPath.None
            or FullVerificationPath.IntegrityOnly
            or FullVerificationPath.PlatformNativeTool)
        {
            return RedumpVerification.NotVerified;
        }

        // Before a Redump match the system is unknown; the structural route is allowed and
        // the matched system is checked again before any claim is made.
        return semantics switch
        {
            MediaSemantics.TrackStructuredCd or MediaSemantics.GdRomTrackLayout => RedumpVerification.TrackVerified,
            MediaSemantics.SingleRawImage2048
                or MediaSemantics.DolphinOpticalImage
                or MediaSemantics.Ps3EncryptedDisc => RedumpVerification.Full,
            _ => RedumpVerification.NotVerified
        };
    }

    private static StorageCompatibility ResolveStorage(
        DiscSystem system,
        DiscContainer container,
        MediaSemantics semantics) =>
        (system, container) switch
        {
            // DuckStation
            (DiscSystem.PlayStation, DiscContainer.CueBin) => StorageCompatibility.Native,
            (DiscSystem.PlayStation, DiscContainer.Chd) => StorageCompatibility.Supported,

            // PCSX2
            (DiscSystem.PlayStation2, DiscContainer.Iso or DiscContainer.CueBin) => StorageCompatibility.Native,
            (DiscSystem.PlayStation2, DiscContainer.Chd or DiscContainer.Cso) => StorageCompatibility.Supported,

            // PPSSPP officially supports CHD (created with chdman createdvd) and CSO.
            (DiscSystem.PlayStationPortable, DiscContainer.Iso) => StorageCompatibility.Native,
            (DiscSystem.PlayStationPortable, DiscContainer.Chd or DiscContainer.Cso) => StorageCompatibility.Supported,

            (DiscSystem.Saturn or DiscSystem.SegaCd or DiscSystem.PcEngineCd or DiscSystem.NeoGeoCd or DiscSystem.ThreeDo,
                DiscContainer.CueBin) => StorageCompatibility.Native,
            (DiscSystem.Saturn or DiscSystem.SegaCd or DiscSystem.PcEngineCd or DiscSystem.NeoGeoCd or DiscSystem.ThreeDo,
                DiscContainer.Chd) => StorageCompatibility.Supported,
            (DiscSystem.JaguarCd, DiscContainer.CueBin) => StorageCompatibility.Native,

            // Flycast
            (DiscSystem.Dreamcast, DiscContainer.Gdi) => StorageCompatibility.Native,
            (DiscSystem.Dreamcast, DiscContainer.Chd) => StorageCompatibility.Supported,

            // Dolphin: RVZ/WIA and plain images are native; CHD/CSO are not Dolphin formats.
            (DiscSystem.GameCube or DiscSystem.Wii, DiscContainer.DolphinCompressed or DiscContainer.Iso)
                => StorageCompatibility.Native,
            (DiscSystem.GameCube, DiscContainer.Gcm) => StorageCompatibility.Native,
            (DiscSystem.GameCube or DiscSystem.Wii, DiscContainer.Chd or DiscContainer.Cso)
                => StorageCompatibility.Unsupported,

            // RPCS3 plays decrypted content; an encrypted Redump ISO needs decryption first.
            (DiscSystem.PlayStation3, DiscContainer.Ps3Folder) => StorageCompatibility.Native,
            (DiscSystem.PlayStation3, DiscContainer.Iso) when semantics == MediaSemantics.Ps3DecryptedIso
                => StorageCompatibility.Supported,
            (DiscSystem.PlayStation3, DiscContainer.Iso) => StorageCompatibility.Unsupported,
            (DiscSystem.PlayStation3, DiscContainer.Chd) => StorageCompatibility.Unsupported,

            _ => StorageCompatibility.Unknown
        };

    private static bool AgreesOnAllHashes(HashedDiscFile file, RedumpReferenceRom rom) =>
        rom.SizeBytes == file.SizeBytes
        && HashEquals(rom.Md5, file.Md5)
        && HashEquals(rom.Sha1, file.Sha1)
        && HashEquals(rom.Crc32, file.Crc32);

    // A missing reference hash never counts as agreement.
    private static bool HashEquals(string? reference, string? actual) =>
        !string.IsNullOrWhiteSpace(reference)
        && !string.IsNullOrWhiteSpace(actual)
        && string.Equals(reference.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool IsDescriptor(string romName)
    {
        string extension = Path.GetExtension(romName ?? string.Empty);
        return extension.Equals(".cue", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gdi", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSystemId(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static Dictionary<string, DiscSystem> BuildSystemIds()
    {
        var map = new Dictionary<string, DiscSystem>(StringComparer.Ordinal);

        void Add(DiscSystem system, params string[] ids)
        {
            foreach (string id in ids)
            {
                map.Add(NormalizeSystemId(id), system);
            }
        }

        Add(DiscSystem.PlayStation, "psx", "Sony - PlayStation");
        Add(DiscSystem.PlayStation2, "ps2", "Sony - PlayStation 2");
        Add(DiscSystem.PlayStationPortable, "psp", "Sony - PlayStation Portable");
        Add(DiscSystem.PlayStation3, "ps3", "Sony - PlayStation 3");
        Add(DiscSystem.Saturn, "ss", "Sega - Saturn");
        Add(DiscSystem.SegaCd, "mcd", "Sega - Mega CD & Sega CD");
        Add(DiscSystem.PcEngineCd, "pce", "NEC - PC Engine CD & TurboGrafx CD");
        Add(DiscSystem.NeoGeoCd, "ngcd", "SNK - Neo Geo CD");
        Add(DiscSystem.ThreeDo, "3do", "Panasonic - 3DO Interactive Multiplayer");
        Add(DiscSystem.JaguarCd, "ajcd", "Atari - Jaguar CD Interactive Multimedia System");
        Add(DiscSystem.Dreamcast, "dc", "Sega - Dreamcast");
        Add(DiscSystem.GameCube, "gc", "Nintendo - GameCube");
        Add(DiscSystem.Wii, "wii", "Nintendo - Wii");
        Add(DiscSystem.Xbox, "xbox", "Microsoft - Xbox");
        Add(DiscSystem.Xbox360, "xbox360", "Microsoft - Xbox 360");

        return map;
    }
}
