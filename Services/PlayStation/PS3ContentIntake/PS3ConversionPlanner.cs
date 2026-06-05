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
                Pipeline: "ISO -> chdman createdvd -> CHD",
                RequiresTemporaryIso: false);
        }

        if (input.InputFormat == PS3InputFormat.Folder && input.CanConvertToChd)
        {
            return new PS3ConversionPlan(
                CanProceed: true,
                Pipeline: "Folder -> temporary ISO -> chdman createdvd -> CHD",
                RequiresTemporaryIso: true);
        }

        if (input.InputFormat == PS3InputFormat.Pkg)
        {
            return new PS3ConversionPlan(
                CanProceed: false,
                Pipeline: "PKG is installable content, not a disc image",
                RequiresTemporaryIso: false);
        }

        return new PS3ConversionPlan(
            CanProceed: false,
            Pipeline: "Unsupported or incomplete PS3 source",
            RequiresTemporaryIso: false);
    }
}
