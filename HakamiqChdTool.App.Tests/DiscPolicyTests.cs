using HakamiqChdTool.App.Core.Chd;
using HakamiqChdTool.App.Core.Verification;
using System;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    private sealed record PolicyRow(
        string Name,
        DiscStructure Structure,
        DiscContainer Container,
        string System,
        MediaSemantics Semantics,
        RedumpFastPath FastPath,
        FastPathState FastPathState,
        FullVerificationPath FullPath,
        StorageCompatibility Storage,
        RedumpVerification MaxProof,
        bool FailClosed);

    // One row per media family and container. Every row asserts semantics, fast path,
    // full-verification path, storage compatibility, Redump proof level and fail-closed.
    private static void TestDiscPolicyMatrix()
    {
        const DiscStructure Cd = DiscStructure.CdTracks;
        const DiscStructure Gd = DiscStructure.GdRomTracks;
        const DiscStructure Image = DiscStructure.SingleImage2048;
        const RedumpFastPath NoFast = RedumpFastPath.None;
        const RedumpFastPath RawFast = RedumpFastPath.RawSha1AndLogicalSize;
        const FastPathState Off = FastPathState.NotAllowed;
        const FastPathState Unproven = FastPathState.CandidateUnproven;

        PolicyRow[] rows =
        [
            // Required rows.
            new("PS1 CD (CHD)", Cd, DiscContainer.Chd, "Sony - PlayStation",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("PS2 CD (CHD)", Cd, DiscContainer.Chd, "Sony - PlayStation 2",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("PS2 DVD (CHD)", Image, DiscContainer.Chd, "Sony - PlayStation 2",
                MediaSemantics.SingleRawImage2048, RawFast, Unproven, FullVerificationPath.ExtractDvdImage,
                StorageCompatibility.Supported, RedumpVerification.Full, false),
            new("PSP UMD (CHD)", Image, DiscContainer.Chd, "Sony - PlayStation Portable",
                MediaSemantics.SingleRawImage2048, RawFast, Unproven, FullVerificationPath.ExtractDvdImage,
                StorageCompatibility.Supported, RedumpVerification.Full, false),
            new("Saturn CD (CHD)", Cd, DiscContainer.Chd, "Sega - Saturn",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("Dreamcast GD-ROM (CHD)", Gd, DiscContainer.Chd, "Sega - Dreamcast",
                MediaSemantics.GdRomTrackLayout, NoFast, Off, FullVerificationPath.ExtractGdRomTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("GameCube ISO", Image, DiscContainer.Iso, "Nintendo - GameCube",
                MediaSemantics.DolphinOpticalImage, NoFast, Off, FullVerificationPath.DirectImageHash,
                StorageCompatibility.Native, RedumpVerification.Full, false),
            new("Wii ISO", Image, DiscContainer.Iso, "Nintendo - Wii",
                MediaSemantics.DolphinOpticalImage, NoFast, Off, FullVerificationPath.DirectImageHash,
                StorageCompatibility.Native, RedumpVerification.Full, false),
            new("PS3 encrypted ISO", Image, DiscContainer.Iso, "Sony - PlayStation 3",
                MediaSemantics.Ps3EncryptedDisc, NoFast, Off, FullVerificationPath.DirectImageHash,
                StorageCompatibility.Unsupported, RedumpVerification.Full, false),
            new("PS3 decrypted ISO", DiscStructure.Ps3DecryptedImage, DiscContainer.Iso, "Sony - PlayStation 3",
                MediaSemantics.Ps3DecryptedIso, NoFast, Off, FullVerificationPath.None,
                StorageCompatibility.Supported, RedumpVerification.NotApplicable, true),
            new("Unknown optical image (untagged CHD)", DiscStructure.Unknown, DiscContainer.Chd, "",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.IntegrityOnly,
                StorageCompatibility.Unknown, RedumpVerification.NotVerified, true),

            // Other track-structured CD systems: never a whole-image shortcut.
            new("Sega CD (CHD)", Cd, DiscContainer.Chd, "Sega - Mega CD & Sega CD",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("PC Engine CD (CHD)", Cd, DiscContainer.Chd, "NEC - PC Engine CD & TurboGrafx CD",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("Neo Geo CD (CHD)", Cd, DiscContainer.Chd, "SNK - Neo Geo CD",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("3DO (CHD)", Cd, DiscContainer.Chd, "Panasonic - 3DO Interactive Multiplayer",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Supported, RedumpVerification.TrackVerified, false),
            new("Jaguar CD (CHD)", Cd, DiscContainer.Chd, "ajcd",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.ExtractCdTracks,
                StorageCompatibility.Unknown, RedumpVerification.TrackVerified, false),
            new("PS1 CD (CUE/BIN)", Cd, DiscContainer.CueBin, "psx",
                MediaSemantics.TrackStructuredCd, NoFast, Off, FullVerificationPath.TrackFileHash,
                StorageCompatibility.Native, RedumpVerification.TrackVerified, false),
            new("Dreamcast GDI", Gd, DiscContainer.Gdi, "dc",
                MediaSemantics.GdRomTrackLayout, NoFast, Off, FullVerificationPath.TrackFileHash,
                StorageCompatibility.Native, RedumpVerification.TrackVerified, false),

            // DVD-style images outside a CHD have no fast path; the image is hashed directly.
            new("PS2 DVD ISO", Image, DiscContainer.Iso, "ps2",
                MediaSemantics.SingleRawImage2048, NoFast, Off, FullVerificationPath.DirectImageHash,
                StorageCompatibility.Native, RedumpVerification.Full, false),
            new("PSP CSO", Image, DiscContainer.Cso, "psp",
                MediaSemantics.SingleRawImage2048, NoFast, Off, FullVerificationPath.DecompressCsoImage,
                StorageCompatibility.Supported, RedumpVerification.Full, false),

            // GameCube/Wii: Dolphin-native formats, never CHD header hashes.
            new("GameCube GCM", DiscStructure.NintendoOpticalImage, DiscContainer.Gcm, "gc",
                MediaSemantics.DolphinOpticalImage, NoFast, Off, FullVerificationPath.DirectImageHash,
                StorageCompatibility.Native, RedumpVerification.Full, false),
            new("Wii RVZ", DiscStructure.DolphinCompressedImage, DiscContainer.DolphinCompressed, "wii",
                MediaSemantics.DolphinOpticalImage, NoFast, Off, FullVerificationPath.PlatformNativeTool,
                StorageCompatibility.Native, RedumpVerification.NotVerified, true),
            new("GameCube CHD", Image, DiscContainer.Chd, "gc",
                MediaSemantics.DolphinOpticalImage, NoFast, Off, FullVerificationPath.IntegrityOnly,
                StorageCompatibility.Unsupported, RedumpVerification.NotVerified, true),

            // PS3 decrypted folder: IRD file check only, never Redump-original.
            new("PS3 JB folder", DiscStructure.Ps3DecryptedFolder, DiscContainer.Ps3Folder, "ps3",
                MediaSemantics.Ps3DecryptedIso, NoFast, Off, FullVerificationPath.Ps3IrdFileCheck,
                StorageCompatibility.Native, RedumpVerification.NotApplicable, true),

            // Specialized platforms and contradictions fail closed.
            new("Xbox ISO", Image, DiscContainer.Iso, "Microsoft - Xbox",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.None,
                StorageCompatibility.Unknown, RedumpVerification.NotVerified, true),
            new("Xbox 360 ISO", Image, DiscContainer.Iso, "Microsoft - Xbox 360",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.None,
                StorageCompatibility.Unknown, RedumpVerification.NotVerified, true),
            new("Dreamcast as DVD image (no generic DVD path)", Image, DiscContainer.Chd, "dc",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.IntegrityOnly,
                StorageCompatibility.Supported, RedumpVerification.NotVerified, true),
            new("PS1 with DVD structure", Image, DiscContainer.Chd, "psx",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.IntegrityOnly,
                StorageCompatibility.Supported, RedumpVerification.NotVerified, true),
            new("PSP with CD structure", Cd, DiscContainer.Chd, "psp",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.IntegrityOnly,
                StorageCompatibility.Supported, RedumpVerification.NotVerified, true),
            new("PS2 with GD-ROM structure", Gd, DiscContainer.Chd, "ps2",
                MediaSemantics.Unknown, NoFast, Off, FullVerificationPath.IntegrityOnly,
                StorageCompatibility.Supported, RedumpVerification.NotVerified, true)
        ];

        foreach (PolicyRow row in rows)
        {
            DiscVerificationPolicy policy = DiscPolicy.Resolve(
                row.Structure,
                row.Container,
                DiscPolicy.ResolveSystem(row.System));

            AssertEqual(row.Semantics, policy.Semantics, $"[{row.Name}] media semantics.");
            AssertEqual(row.FastPath, policy.FastPath, $"[{row.Name}] allowed fast path.");
            AssertEqual(row.FastPathState, policy.FastPathState, $"[{row.Name}] fast path state.");
            AssertFalse(policy.CanUseFastPath, $"[{row.Name}] no fast path may be usable without a corpus proof.");
            AssertEqual(row.FullPath, policy.FullPath, $"[{row.Name}] full-verification path.");
            AssertEqual(row.Storage, policy.Storage, $"[{row.Name}] storage compatibility.");
            AssertEqual(row.MaxProof, policy.MaxProof, $"[{row.Name}] Redump proof level.");
            AssertEqual(row.FailClosed, policy.FailClosed, $"[{row.Name}] fail-closed behavior.");
        }

        // A proven registry enables only the proven system's CHD fast path.
        var pspProven = new FastPathProofRegistry([DiscSystem.PlayStationPortable]);
        DiscVerificationPolicy pspChd = DiscPolicy.Resolve(Image, DiscContainer.Chd, DiscSystem.PlayStationPortable, pspProven);
        AssertEqual(FastPathState.Proven, pspChd.FastPathState, "PSP CHD fast path is usable once proven.");
        AssertTrue(pspChd.CanUseFastPath, "PSP CHD fast path is usable once proven.");

        DiscVerificationPolicy ps2Chd = DiscPolicy.Resolve(Image, DiscContainer.Chd, DiscSystem.PlayStation2, pspProven);
        AssertEqual(FastPathState.CandidateUnproven, ps2Chd.FastPathState, "A PSP proof must not enable the PS2 fast path.");

        DiscVerificationPolicy ps1Chd = DiscPolicy.Resolve(Cd, DiscContainer.Chd, DiscSystem.PlayStation, new FastPathProofRegistry([DiscSystem.PlayStation]));
        AssertEqual(RedumpFastPath.None, ps1Chd.FastPath, "Track-structured CDs never get a whole-image fast path, even if registered.");

        AssertFalse(
            FastPathProofRegistry.Production.IsProven(DiscSystem.PlayStationPortable)
            || FastPathProofRegistry.Production.IsProven(DiscSystem.PlayStation2),
            "No production fast path may be enabled before a reviewed corpus proof.");
    }

    private static void TestDiscPolicyProofAssessment()
    {
        DiscVerificationPolicy dvd = DiscPolicy.Resolve(DiscStructure.SingleImage2048, DiscContainer.Iso, DiscSystem.PlayStation2);
        DiscVerificationPolicy cd = DiscPolicy.Resolve(DiscStructure.CdTracks, DiscContainer.CueBin, DiscSystem.PlayStation);

        var image = new HashedDiscFile(4096, "aa", "bb", "cc");
        var imageRom = new RedumpReferenceRom("game.iso", 4096, "aa", "bb", "cc");

        AssertEqual(RedumpVerification.Full, DiscPolicy.AssessProof(dvd, [image], [imageRom]), "All hashes and size agree -> Full.");
        AssertEqual(
            RedumpVerification.NotVerified,
            DiscPolicy.AssessProof(dvd, [image], [imageRom with { Sha1 = "zz" }]),
            "A CRC/MD5 match with a different SHA-1 is not Redump proof.");
        AssertEqual(
            RedumpVerification.NotVerified,
            DiscPolicy.AssessProof(dvd, [image], [imageRom with { Md5 = null }]),
            "A missing reference hash is not agreement.");
        AssertEqual(
            RedumpVerification.NotVerified,
            DiscPolicy.AssessProof(dvd, [image], [imageRom with { SizeBytes = 2048 }]),
            "Size must agree.");

        var track1 = new HashedDiscFile(2352, "m1", "s1", "c1");
        var track2 = new HashedDiscFile(4704, "m2", "s2", "c2");
        RedumpReferenceRom[] discRoms =
        [
            new("Game (Track 1).bin", 2352, "m1", "s1", "c1"),
            new("Game (Track 2).bin", 4704, "m2", "s2", "c2"),
            new("Game.cue", 120, "mc", "sc", "cc")
        ];

        AssertEqual(RedumpVerification.TrackVerified, DiscPolicy.AssessProof(cd, [track1, track2], discRoms), "Every track present -> TrackVerified.");
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessProof(cd, [track1], discRoms), "A missing track is not verified.");
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessProof(cd, [track1, track1], discRoms), "A duplicated track cannot stand in for another.");

        DiscVerificationPolicy decrypted = DiscPolicy.Resolve(DiscStructure.Ps3DecryptedImage, DiscContainer.Iso, DiscSystem.PlayStation3);
        AssertEqual(RedumpVerification.NotApplicable, DiscPolicy.AssessProof(decrypted, [image], [imageRom]), "Decrypted PS3 is never Redump-original.");

        DiscVerificationPolicy xbox = DiscPolicy.Resolve(DiscStructure.SingleImage2048, DiscContainer.Iso, DiscSystem.Xbox);
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessProof(xbox, [image], [imageRom]), "Xbox matches never become Redump proof.");

        // Fast path decisions.
        var proven = new FastPathProofRegistry([DiscSystem.PlayStationPortable]);
        DiscVerificationPolicy pspChd = DiscPolicy.Resolve(DiscStructure.SingleImage2048, DiscContainer.Chd, DiscSystem.PlayStationPortable, proven);
        DiscVerificationPolicy pspUnproven = DiscPolicy.Resolve(DiscStructure.SingleImage2048, DiscContainer.Chd, DiscSystem.PlayStationPortable);

        AssertEqual(RedumpVerification.RawSha1AndSize, DiscPolicy.AssessFastPath(pspChd, "BB", 4096, [imageRom]), "Unique proven hit -> RawSha1AndSize.");
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessFastPath(pspUnproven, "bb", 4096, [imageRom]), "Unproven fast path is never used.");
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessFastPath(pspChd, "bb", 4096, [imageRom, imageRom with { RomName = "other.iso" }]), "An ambiguous hit falls back to full verification.");
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessFastPath(pspChd, "bb", 8192, [imageRom]), "Logical size must equal the Redump size.");
        AssertEqual(RedumpVerification.NotVerified, DiscPolicy.AssessFastPath(pspChd, "bb", null, [imageRom]), "A missing logical size falls back.");
    }

    private static void TestDiscPolicySystemResolution()
    {
        (string Id, DiscSystem Expected)[] cases =
        [
            ("Sony - PlayStation", DiscSystem.PlayStation),
            ("Sony - PlayStation 2", DiscSystem.PlayStation2),
            ("Sony - PlayStation Portable", DiscSystem.PlayStationPortable),
            ("psx", DiscSystem.PlayStation),
            ("PS2", DiscSystem.PlayStation2),
            ("Sega - Dreamcast", DiscSystem.Dreamcast),
            ("Microsoft - Xbox 360", DiscSystem.Xbox360),
            ("Microsoft - Xbox", DiscSystem.Xbox),
            ("Nintendo - Wii", DiscSystem.Wii),
            ("PlayStation", DiscSystem.Unknown),
            ("Sega - Naomi", DiscSystem.Unknown),
            ("Test System", DiscSystem.Unknown),
            ("", DiscSystem.Unknown)
        ];

        foreach ((string id, DiscSystem expected) in cases)
        {
            AssertEqual(expected, DiscPolicy.ResolveSystem(id), $"System resolution for '{id}'.");
        }
    }

    private static void TestChdMetadataStructure()
    {
        (string Output, DiscStructure Expected)[] cases =
        [
            ("Logical size: 1,048,576 bytes\nMetadata:     Tag='DVD '  Index=0  Length=1 bytes\n", DiscStructure.SingleImage2048),
            ("Metadata:     Tag='CHT2'  Index=0  Length=90 bytes\n              TRACK:1 TYPE:MODE2_RAW\nMetadata:     Tag='CHT2'  Index=1  Length=86 bytes\n", DiscStructure.CdTracks),
            ("Metadata:     Tag='CHGD'  Index=0  Length=90 bytes\n", DiscStructure.GdRomTracks),
            ("Metadata:     Tag='CHT2'  Index=0\nMetadata:     Tag='DVD '  Index=0\n", DiscStructure.Unknown),
            // No disc metadata: a large logical size or a media-type guess is not evidence.
            ("Logical size: 4,700,000,000 bytes\nMedia type: DVD-ROM\n", DiscStructure.Unknown),
            ("Hunk Size: 4,096 bytes\n", DiscStructure.Unknown),
            ("", DiscStructure.Unknown)
        ];

        foreach ((string output, DiscStructure expected) in cases)
        {
            AssertEqual(expected, ChdMetaTags.DetectStructure(output), "CHD metadata structure for: " + output.Replace('\n', '|'));
        }
    }
}
