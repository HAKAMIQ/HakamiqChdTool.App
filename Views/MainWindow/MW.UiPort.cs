using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

using HakamiqChdTool.App.QueueRun;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;

namespace HakamiqChdTool.App;

public partial class MainWindow
{
    private sealed class UiPortAdapter : IQueueRunUiPort
    {
        private readonly MainWindow _w;

        public UiPortAdapter(MainWindow window)
        {
            ArgumentNullException.ThrowIfNull(window);

            _w = window;
        }

        public Window Owner => _w;

        public bool IncludeSubfolders => _w._settings.IncludeSubfolders;

        public bool IsQueueInteractionLocked => _w.IsQueueInteractionLocked;

        public bool CanUseAppFeature(AppFeature feature)
        {
            return _w._appFeatureService.IsEnabled(feature);
        }

        public bool RequireAppFeature(AppFeature feature)
        {
            return _w.RequireAppFeature(feature);
        }

        public QueueExecutionProfile GetSelectedInputExecutionProfile()
        {
            return _w.GetSelectedInputExecutionProfileFromUi();
        }

        public bool IsSelectedScanMode()
        {
            return _w.IsSelectedScanModeFromUi();
        }

        public string GetSelectedInputDialogTitle(QueueExecutionProfile profile)
        {
            return _w.GetSelectedInputDialogTitleFromUi(profile);
        }

        public string GetSelectedInputDialogFilter(QueueExecutionProfile profile)
        {
            return _w.GetSelectedInputDialogFilterFromUi(profile);
        }

        public string GetSelectedFolderDialogDescription()
        {
            return _w.GetSelectedFolderDialogDescriptionFromUi();
        }

        public void SetFooterStatus(string messageEnglish)
        {
            _w.SetFooterStatus(messageEnglish);
        }

        public void UpdateUiState()
        {
            _w.UpdateUiState();
        }

        public void ShowError(string title, string body)
        {
            _w.ShowNoticeDialog(title, body);
        }

        public void CaptureThemeIntoSettings()
        {
            _w.CaptureThemeIntoSettings();
        }

        public void PersistSettings()
        {
            _w.PersistSettings();
        }

        public bool ConfirmStorageAdvisorBeforeProcessing(
            IReadOnlyList<TaskQueueItemViewModel> items,
            bool processedSelectionOnly)
        {
            ArgumentNullException.ThrowIfNull(items);

            if (items.Count == 0 || _w._settings.SuppressStorageAdvisorDialog)
            {
                return true;
            }

            return _w.ConfirmStorageAdvisorBeforeProcessing(
                items,
                processedSelectionOnly);
        }

        public void ResetRunSummary()
        {
            _w.ResetRunSummary();
        }

        public void BuildRunSummary(
            bool showCompletionDialog,
            bool wasCancelled,
            bool processedSelectionOnly,
            PostConversionArtifactResult runArtifacts)
        {
            _w.BuildRunSummary(
                showCompletionDialog,
                wasCancelled,
                processedSelectionOnly,
                runArtifacts);
        }

        public Task<PostConversionArtifactResult> GenerateM3uPlaylistsForCompletedChdOutputsAsync(
            IReadOnlyList<string> outputPaths)
        {
            return _w.GenerateM3uPlaylistsForCompletedChdOutputsAsync(outputPaths);
        }

        public Task ApplyAutoStandardizedNameIfEnabledAsync(TaskQueueItemViewModel item)
        {
            return _w.ApplyAutoStandardizedNameIfEnabled(item);
        }

        public void SetHasActiveQueueBindingAndSync(
            TaskQueueItemViewModel item,
            bool value)
        {
            _w.SetHasActiveQueueBindingAndSync(item, value);
        }

        public bool IsRowQueuedForProcessing(Guid itemId)
        {
            return _w._queueRowStore.GetById(itemId) is { } row &&
                _w.IsRowQueuedForProcessingForSelectedMode(row);
        }

        public Task IngestPathsAsync(
            IEnumerable<string> paths,
            QueueIngestKind inputKind,
            QueueIntakeSource intakeSource)
        {
            return _w._viewModel.IngestPathsAsync(
                paths,
                inputKind,
                intakeSource);
        }

        public Task<IReadOnlyList<Guid>> IngestQuickPathsAsync(
            IEnumerable<string> paths,
            QueueIngestKind inputKind,
            QueueExecutionProfile executionProfile,
            QueueIntakeSource intakeSource)
        {
            return _w._viewModel.IngestQuickPathsAsync(
                paths,
                inputKind,
                executionProfile,
                intakeSource);
        }

        public TaskQueueItemViewModel? TryMaterializeOrGet(Guid id)
        {
            TaskQueueItemViewModel? vm = _w._viewport.TryGetMaterialized(id);
            if (vm is not null)
            {
                return vm;
            }

            int index = _w._queueRowStore.IndexOf(id);
            return index >= 0 ? _w._viewport.Realize(index) : null;
        }

        public IReadOnlyList<Guid> EnumerateQueuedItemIds()
        {
            var ids = new List<Guid>();

            foreach (QueueRowData row in _w._queueRowStore.Rows)
            {
                if (_w.IsRowQueuedForProcessingForSelectedMode(row))
                {
                    ids.Add(row.ItemId);
                }
            }

            return ids;
        }
    }
}