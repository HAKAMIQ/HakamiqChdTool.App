using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services.PostProcessing;
using Serilog;
using System;
using System.IO;

namespace HakamiqChdTool.App.Core.Workflow;

internal sealed class WorkflowPostProcessingStage
{
    private readonly PostConversionArtifactService _artifacts;
    private readonly ILogger _log;

    public WorkflowPostProcessingStage(PostConversionArtifactService artifacts, ILogger log)
    {
        ArgumentNullException.ThrowIfNull(artifacts);
        ArgumentNullException.ThrowIfNull(log);

        _artifacts = artifacts;
        _log = log;
    }

    public PostConversionArtifactResult RunAfterVerifiedConversion(AppSettings settings, string workingInputPath, string finalOutputPath)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.CopyMatchingSbi)
        {
            return PostConversionArtifactResult.Empty;
        }

        try
        {
            return _artifacts.CopyMatchingSbiIfExists(workingInputPath, finalOutputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or PathTooLongException)
        {
            _log.Warning(
                ex,
                "Post-conversion artifact processing failed. Input={Input}; Output={Output}",
                workingInputPath,
                finalOutputPath);

            return PostConversionArtifactResult.WithFailure(
                "SBI",
                "LocPostProcessing_SbiCopyFailed",
                finalOutputPath);
        }
    }
}
