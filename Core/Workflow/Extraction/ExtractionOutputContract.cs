using HakamiqChdTool.App.Models.Chd;
using System;

namespace HakamiqChdTool.App.Core.Workflow.Extraction;

internal sealed record ExtractionOutputContract(
    ExtractionOutputKind Kind,
    string PendingPrimaryPath,
    string FinalPrimaryPath)
{
    public bool IsCueBinBundle => Kind == ExtractionOutputKind.CueBinBundle;

    public static ExtractionOutputContract Create(
        ChdmanExtractionKind finalizationKind,
        string pendingPrimaryPath,
        string finalPrimaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pendingPrimaryPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(finalPrimaryPath);

        ExtractionOutputKind kind = finalizationKind == ChdmanExtractionKind.ExtractCd
            ? ExtractionOutputKind.CueBinBundle
            : ExtractionOutputKind.SingleFile;

        return new ExtractionOutputContract(kind, pendingPrimaryPath, finalPrimaryPath);
    }
}
