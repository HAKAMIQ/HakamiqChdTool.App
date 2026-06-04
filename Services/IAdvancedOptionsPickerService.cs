namespace HakamiqChdTool.App.Services;

public interface IAdvancedOptionsPickerService
{
    string? PickFolder(string titleKey, string? selectedPath);

    string? PickFile(string titleKey, string filterKey, string? currentPath, string fallbackDirectory);
}