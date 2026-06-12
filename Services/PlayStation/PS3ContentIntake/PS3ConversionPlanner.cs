using HakamiqChdTool.App.Models.PlayStation;

namespace HakamiqChdTool.App.Services.PlayStation.PS3ContentIntake;

public sealed class PS3ConversionPlanner
{
    public PS3ConversionPlan CreatePlan(PS3ContentIntakeResult input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (input.InputFormat == PS3InputFormat.Iso && input.CanConvertToChd)
        {
            return new PS3ConversionPlan(
                CanProceed: true,
                Pipeline: PS3ContentIntakeMessages.PipelineIsoToChd,
                RequiresTemporaryIso: false);
        }

        if (input.InputFormat == PS3InputFormat.Folder && input.CanConvertToChd)
        {
            return new PS3ConversionPlan(
                CanProceed: true,
                Pipeline: PS3ContentIntakeMessages.PipelineFolderToChd,
                RequiresTemporaryIso: true);
        }

        if (input.InputFormat == PS3InputFormat.Pkg)
        {
            return new PS3ConversionPlan(
                CanProceed: false,
                Pipeline: PS3ContentIntakeMessages.PipelinePkgNotDiscImage,
                RequiresTemporaryIso: false);
        }

        return new PS3ConversionPlan(
            CanProceed: false,
            Pipeline: PS3ContentIntakeMessages.PipelineUnsupportedSource,
            RequiresTemporaryIso: false);
    }
}
