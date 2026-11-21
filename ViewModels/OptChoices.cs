
namespace HakamiqChdTool.App.ViewModels;

public sealed class ProcessorOption(int value, string label)
{
    public int Value { get; } = value;

    public string Label { get; } = label;
}

public sealed class ChoiceOption(string key, string label, string description)
{
    public string Key { get; } = key;

    public string Label { get; } = label;

    public string Description { get; } = description;
}
