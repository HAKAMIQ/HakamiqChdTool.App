using HakamiqChdTool.App.Models;
using System;
using System.IO;

namespace HakamiqChdTool.App.Services;

public sealed record RestoreTargetRequest(
    MetadataAwareChdExtractionDecision ExtractionDecision,
    string PendingOutputPath,
    string FinalOutputPath);

public sealed record RestoreTargetDecision(
    string ChdmanOutputPath,
    string? ExtractCdCueOutputPath,
    string? ExtractCdBinOutputPath,
    bool VerifyExtractCdCueBinContract,
    ChdmanExtractionKind FinalizationKind,
    ChdmanExtractionKind SuccessKind,
    string SuccessMessageKey)
{
    public bool UsesLegacyCdToIsoRestore => !VerifyExtractCdCueBinContract
        && FinalizationKind == ChdmanExtractionKind.ExtractDvd
        && SuccessKind == ChdmanExtractionKind.ExtractDvd;
}

public interface IRestoreTargetPolicy
{
    RestoreTargetDecision Resolve(RestoreTargetRequest request);
}

public sealed class RestoreTargetPolicy : IRestoreTargetPolicy
{
    public RestoreTargetDecision Resolve(RestoreTargetRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ExtractionDecision);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PendingOutputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FinalOutputPath);

        if (request.ExtractionDecision.RestoreTargetMode == ChdRestoreTargetMode.LegacyCdProfileToIso)
        {
            string cuePath = BuildLegacyTempCuePath(request.PendingOutputPath);
            return new RestoreTargetDecision(
                request.PendingOutputPath,
                cuePath,
                request.PendingOutputPath,
                VerifyExtractCdCueBinContract: false,
                FinalizationKind: ChdmanExtractionKind.ExtractDvd,
                SuccessKind: ChdmanExtractionKind.ExtractDvd,
                SuccessMessageKey: request.ExtractionDecision.SuccessMessageKey);
        }

        return new RestoreTargetDecision(
            request.PendingOutputPath,
            null,
            null,
            VerifyExtractCdCueBinContract: true,
            request.ExtractionDecision.ExtractionKind,
            request.ExtractionDecision.ExtractionKind,
            request.ExtractionDecision.SuccessMessageKey);
    }

    private static string BuildLegacyTempCuePath(string pendingOutputPath)
    {
        string? directory = Path.GetDirectoryName(pendingOutputPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("LocConversion_OutputDirectoryMissing");
        }

        string stem = Path.GetFileNameWithoutExtension(pendingOutputPath);
        stem = string.IsNullOrWhiteSpace(stem) ? "restore" : stem;

        return Path.Combine(directory, $"{stem}.legacy-restore.cue");
    }
}
