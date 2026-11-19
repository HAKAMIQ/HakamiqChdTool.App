using HakamiqChdTool.App.Core.Input;
using HakamiqChdTool.App.Models.Chd;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.ExternalTools;
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;

namespace HakamiqChdTool.App.Tests;

internal static partial class Program
{
    // A ZSO inside a ZIP is picked as the archive's disc image, extracted, planned as ZSO and
    // converted through CsoKit and chdman like a loose ZSO. An archive with a CSO and a ZSO is
    // refused as ambiguous.
    private static void TestZsoInsideArchiveConverts(AppReflection app, string workDirectory)
    {
        var extractor = new SevenZipArchiveExtractionService();
        AssertTrue(extractor.IsAvailable, "The bundled 7-Zip is available.");

        string root = Path.Combine(workDirectory, "zso-archive");
        Directory.CreateDirectory(root);
        byte[] iso = CreateIsoPayload(48, 21);
        string zip = Path.Combine(root, "game.zip");
        using (ZipArchive archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            using (Stream entry = archive.CreateEntry("Game (USA)/game.zso").Open())
            {
                entry.Write(BuildStoredZso(iso));
            }

            AddZipText(archive, "Game (USA)/readme.txt", "notes");
        }

        AssertEqual(QueueInputRole.ArchiveContainer, QueueInputClassifier.Classify(zip).Role, "The ZIP enters the queue as an archive.");

        string extractDir = Path.Combine(root, "extracted");
        Directory.CreateDirectory(extractDir);
        ArchiveExtractionResult extraction = extractor.ExtractFirstConvertibleDiscImageAsync(zip, extractDir).GetAwaiter().GetResult();
        AssertTrue(extraction.IsSuccess, "The ZSO is found and extracted: " + extraction.Message);
        AssertEqual(".zso", Path.GetExtension(extraction.ExtractedPath), "The extracted disc image is the ZSO.");
        AssertTrue(QueueInputClassifier.Classify(extraction.ExtractedPath).IsConvertibleDiscImage, "The extracted ZSO is convertible.");

        ChdWorkflowProfilePlan plan = ChdWorkflowProfilePlanner.PlanCreateFromSource(
            extraction.ExtractedPath,
            IsoCreateCommandOverride.Auto,
            ChdMediaContainerKind.Archive);
        AssertTrue(plan.IsSupported && plan.Command == "createdvd" && plan.MediaKind == ChdMediaFormatKind.Zso,
            "The extracted ZSO plans createdvd: " + plan.FailureMessage);

        string chdmanPath = app.GetRuntimeChdmanPath(app.CreateRuntimeToolService());
        var service = new ChdConversionService(
            new ChdCommandPreparationService(),
            new ChdProcessExecutionService(),
            new ChdResultMappingService(),
            new ChdVerificationBridge(),
            new ChdmanCapabilityService(),
            null,
            new PlatformAwareChdProfilePolicy(),
            new CsoPreprocessor(
                new CsoToolProbe(new CsoToolLocator(preferredToolPath: null), new ExternalToolProcessRunner()),
                new ExternalToolProcessRunner()));

        string outDir = Path.Combine(root, "out");
        Directory.CreateDirectory(outDir);
        string chd = Path.Combine(outDir, "game.chd");
        ChdConversionResult created = service.ConvertToChdAsync(chdmanPath, extraction.ExtractedPath, chd).GetAwaiter().GetResult();
        AssertTrue(created.IsSuccess, "The archived ZSO converts to CHD: " + created.Message + " " + created.Error);

        string restored = Path.Combine(outDir, "restored.iso");
        ChdConversionResult extracted = service.ConvertToChdAsync(
                chdmanPath, chd, restored,
                extractionKind: ChdmanExtractionKind.ExtractDvd,
                extractionMetadataDecisionConfirmed: true)
            .GetAwaiter().GetResult();
        AssertTrue(extracted.IsSuccess, "extractdvd succeeds: " + extracted.Message + " " + extracted.Error);
        AssertEqual(Convert.ToHexString(SHA256.HashData(iso)), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(restored))),
            "The ISO inside the archived ZSO survives the round trip byte for byte.");

        string mixed = Path.Combine(root, "mixed.zip");
        using (ZipArchive archive = ZipFile.Open(mixed, ZipArchiveMode.Create))
        {
            using (Stream entry = archive.CreateEntry("a.zso").Open())
            {
                entry.Write(BuildStoredZso(iso));
            }

            AddZipText(archive, "b.cso", "CISO placeholder");
        }

        string mixedDir = Path.Combine(root, "mixed-out");
        Directory.CreateDirectory(mixedDir);
        ArchiveExtractionResult ambiguous = extractor.ExtractFirstConvertibleDiscImageAsync(mixed, mixedDir).GetAwaiter().GetResult();
        AssertFalse(ambiguous.IsSuccess, "An archive with both a CSO and a ZSO is refused as ambiguous.");
    }
}
