using CommunityToolkit.Mvvm.ComponentModel;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using System.Globalization;

namespace HakamiqChdTool.App.ViewModels;

public partial class QueueItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ProgressText))]
    private int _progress;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private ProcessingState _processingState = ProcessingState.Idle;

    [ObservableProperty]
    private string _statePhaseArabic = ArabicUi.ProcessingPhaseHeadline(ProcessingState.Idle);

    [ObservableProperty]
    private string? _errorMessage;

    public string ProgressText => string.Create(CultureInfo.InvariantCulture, $"{Progress}%");

    partial void OnProcessingStateChanged(ProcessingState value)
    {
        StatePhaseArabic = ArabicUi.ProcessingPhaseHeadline(value);
    }
}