namespace HakamiqChdTool.App.Core.Input;

public interface IMediaInputClassifier
{
    MediaInputDescriptor Classify(string? path);
}
