using HakamiqChdTool.App.Models.Chd;
using System;
using System.IO;

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

        // A CD/GD-ROM extraction is a CUE/BIN bundle, or a GDI bundle when the output is a .gdi.
        ExtractionOutputKind kind = finalizationKind != ChdmanExtractionKind.ExtractCd
            ? ExtractionOutputKind.SingleFile
            : string.Equals(Path.GetExtension(finalPrimaryPath), ".gdi", StringComparison.OrdinalIgnoreCase)
                ? ExtractionOutputKind.GdiBundle
                : ExtractionOutputKind.CueBinBundle;

        return new ExtractionOutputContract(kind, pendingPrimaryPath, finalPrimaryPath);
    }
}
