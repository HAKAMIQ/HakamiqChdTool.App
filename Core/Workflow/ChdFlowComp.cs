using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Conversion;
using HakamiqChdTool.App.Services.Power;
using HakamiqChdTool.App.Services.Storage;
using Serilog;
using System;

namespace HakamiqChdTool.App.Core.Workflow;

public sealed partial class ChdWorkflowOrchestrator
{
    private static IConversionPowerGuard CreatePowerGuard(ILogger log)
    {
        return new WindowsConversionPowerGuard(log);
    }

    private static WorkflowConversionStage CreateConversionStage(
        ChdConversionService conversion,
        ChdVerificationService verify,
        WorkflowPostProcessingStage postProcessingStage,
        IConversionPowerGuard powerGuard,
        ILogger log)
    {
        ArgumentNullException.ThrowIfNull(powerGuard);

        ConversionSessionGuard conversionSessionGuard = new(
            powerGuard,
            new StorageTemperatureMonitor(log),
            new StorageHealthPolicy(),
            log);

        return new WorkflowConversionStage(
            conversion,
            verify,
            postProcessingStage,
            conversionSessionGuard,
            new StorageTopologyService(),
            new ConversionSafetyPolicy(log),
            new ConversionPerformanceReportFactory(),
            log);
    }
}
