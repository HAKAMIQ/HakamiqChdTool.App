using System;
using System.Linq;
using System.Windows;

using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private void OperationModeRadio_Click(object sender, RoutedEventArgs e)
    {
        QueueWorkspace.OperationModeKey = ActionRail.DropHintModeKey;

        ApplySelectedOperationModeToWaitingRows();
        UpdateUiState();
    }

    private void ApplySelectedOperationModeToWaitingRows()
    {
        QueueOperationMode selectedMode = GetSelectedQueueOperationMode();
        if (selectedMode == QueueOperationMode.None || _queueRowStore.Count == 0)
        {
            return;
        }

        Guid[] rowIds =
        [
            .. _queueRowStore.Rows.Select(row => row.ItemId)
        ];

        foreach (Guid rowId in rowIds)
        {
            QueueRowData? row = _queueRowStore.GetById(rowId);
            if (row is null)
            {
                continue;
            }

            bool nextVisible = row.OperationIntent == QueueOperationMode.None
                ? string.Equals(
                    row.RequestedAction,
                    TaskActionCodes.Unsupported,
                    StringComparison.Ordinal)
                    || QueueModeResolver.IsPathVisibleForMode(
                        row.OriginalPath,
                        selectedMode)
                : row.OperationIntent == selectedMode;

            if (row.IsVisibleInCurrentOperationMode == nextVisible)
            {
                continue;
            }

            _queueRowStore.Mutate(
                row.ItemId,
                target => target.IsVisibleInCurrentOperationMode = nextVisible);
        }

        _queueView.RefreshView();
        _queueUiAggregates.Rebuild(_queueRowStore.Rows);

        ResetSelectionAfterQueueFilterChanged();
    }

    private void ResetSelectionAfterQueueFilterChanged()
    {
        if (_queueView.Count == 0)
        {
            TasksDataGrid.SelectedItem = null;
            _viewModel.SelectedTask = null;
            return;
        }

        if (TasksDataGrid.SelectedItem is TaskQueueItemViewModel selected &&
            _queueRowStore.GetById(selected.QueueItemId) is { IsVisibleInCurrentOperationMode: true })
        {
            return;
        }

        TasksDataGrid.SelectedIndex = 0;
        _viewModel.SelectedTask = TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
    }

    private QueueOperationMode GetSelectedQueueOperationMode()
    {
        if (ActionRail.IsVerifyMode)
        {
            return QueueOperationMode.Verify;
        }

        if (ActionRail.IsExtractMode)
        {
            return QueueOperationMode.Extract;
        }

        if (ActionRail.IsConvertMode)
        {
            return QueueOperationMode.Convert;
        }

        return QueueOperationMode.None;
    }
}
