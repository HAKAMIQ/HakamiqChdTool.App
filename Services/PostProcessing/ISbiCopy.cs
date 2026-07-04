using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Services.PostProcessing;

public interface ISbiArtifactCopier
{
    PostConversionArtifactResult CopyMatchingSbiIfExists(string workingInputPath, string outputChdPath);
}
