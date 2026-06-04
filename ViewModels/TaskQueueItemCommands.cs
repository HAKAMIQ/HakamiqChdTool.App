using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;

namespace HakamiqChdTool.App.ViewModels;

public sealed partial class TaskQueueItemViewModel
{
    public IReadOnlyList<QueueOperationChoice> OperationChoices => _operationChoices;

    public string? SelectedOperationBinding
    {
        get => RequestedAction == TaskActionCodes.PendingSelection ? null : RequestedAction;
        set
        {
            if (string.IsNullOrWhiteSpace(value) || value == TaskActionCodes.PendingSelection)
            {
                return;
            }

            if (!QueueItemOperationCatalog.IsOperationAllowed(GetOperationCatalogInputPath(), value))
            {
                return;
            }

            RequestedAction = value;
            if (CurrentState == TaskQueueStateCodes.AwaitingOperationSelection)
            {
                CurrentState = TaskQueueStateCodes.Pending;
                StatusDetail = MainWindowMessages.ReadyForProcessing;
            }
        }
    }

    public string SelectedOperationDisplay => BuildDisplayPlannedAction();

    public string DisplayPlannedAction => SelectedOperationDisplay;

    private string GetOperationCatalogInputPath()
    {
        if (!string.IsNullOrWhiteSpace(_originalPath))
        {
            return _originalPath;
        }

        return string.IsNullOrWhiteSpace(_workingPath) ? string.Empty : _workingPath;
    }

    private void RebuildOperationCatalog()
    {
        IReadOnlyList<string> codes = QueueItemOperationCatalog.GetSupportedOperationCodes(GetOperationCatalogInputPath());
        _supportedOperationCodes = codes.ToArray();
        CoerceRequestedActionToSupportedCatalog();
        _operationChoices = _supportedOperationCodes
            .Select(c => new QueueOperationChoice { Code = c, Label = ArabicUi.GetActionArabicLabel(c) })
            .ToArray();
        OnPropertyChanged(nameof(OperationChoices));
        OnPropertyChanged(nameof(DisplayAvailableOperationsText));
        OnPropertyChanged(nameof(HasSingleSupportedOperation));
        OnPropertyChanged(nameof(HasMultipleSupportedOperations));
        OnPropertyChanged(nameof(ShowInlineFixedAction));
        OnPropertyChanged(nameof(ShowOperationCombo));
        OnPropertyChanged(nameof(ShowActionPlaceholder));
        OnPropertyChanged(nameof(SelectedOperationBinding));
        NotifyUiCardLayoutProperties();
    }

    private void CoerceRequestedActionToSupportedCatalog()
    {
        if (_supportedOperationCodes.Length == 0)
        {
            if (!string.Equals(_requestedAction, TaskActionCodes.Unsupported, StringComparison.Ordinal))
            {
                RequestedAction = TaskActionCodes.Unsupported;
            }

            return;
        }

        if (_supportedOperationCodes.Length == 1)
        {
            string only = _supportedOperationCodes[0];
            if (!string.Equals(_requestedAction, only, StringComparison.Ordinal))
            {
                RequestedAction = only;
            }

            return;
        }

        if (string.Equals(_requestedAction, TaskActionCodes.PendingSelection, StringComparison.Ordinal))
        {
            return;
        }

        for (int i = 0; i < _supportedOperationCodes.Length; i++)
        {
            if (string.Equals(_supportedOperationCodes[i], _requestedAction, StringComparison.Ordinal))
            {
                return;
            }
        }

        RequestedAction = TaskActionCodes.PendingSelection;
    }

    private string BuildDisplayAvailableOperationsText()
    {
        if (_supportedOperationCodes.Length == 0)
        {
            return ArabicUi.Get("LocQueue_NoOperationsAvailable");
        }

        return string.Join("، ", _supportedOperationCodes.Select(static c => ArabicUi.GetActionArabicLabel(c)));
    }
}
