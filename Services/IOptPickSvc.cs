namespace HakamiqChdTool.App.Services;

public interface IOptionsPickerService
{
    string? PickFolder(string titleKey, string? selectedPath);

    string? PickFile(string titleKey, string filterKey, string? currentPath, string fallbackDirectory);
}