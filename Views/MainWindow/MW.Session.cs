using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{

    private sealed class SessionAdapter : IMainWindowSession
    {
        private readonly MainWindow _w;

        public SessionAdapter(MainWindow window)
        {
            ArgumentNullException.ThrowIfNull(window);

            _w = window;
        }

        public Window Owner => _w;

        public Dispatcher UiDispatcher => _w.Dispatcher;

        public bool IsQueueInteractionLocked => _w.IsQueueInteractionLocked;

        public bool IsProcessing => _w._coordinator?.IsProcessing ?? false;

        public bool CancellationRequested => _w._coordinator?.CancellationRequested ?? false;

        public bool IncludeSubfolders => _w._settings.IncludeSubfolders;

        public AppSettings GetSettings()
        {
            return _w._settings;
        }

        public IAppFeatureService AppFeatures => _w._appFeatureService;

        public bool RequireAppFeature(AppFeature feature)
        {
            return _w.RequireAppFeature(feature);
        }

        public QueueRowStore QueueRows => _w._queueRowStore;

        public (bool IsCompliant, string SuggestedStandardName) AnalyzeNamingForPath(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                return (true, string.Empty);
            }

            AppSettings settings = _w._settings;
            if (!settings.EnableDeepIntegrityCheck ||
                !_w._appFeatureService.IsEnabled(AppFeature.StandardNamingSuggestion))
            {
                return (true, string.Empty);
            }

            RedumpSqliteManager database = RedumpSqliteManager.Default;
            database.EnsureInitialized();

            if (!database.HasAnyRows())
            {
                return (true, string.Empty);
            }

            (bool compliant, string suggested) = NamingCorrectionEngine.Analyze(sourcePath);
            return (compliant, suggested);
        }

        public void SetFooterStatus(string messageEnglish)
        {
            _w.SetFooterStatus(messageEnglish);
        }

        public void SetFooterIntakeProgress(
            string stageText,
            int scannedCount,
            int totalCount,
            int acceptedCount,
            bool hasKnownTotal)
        {
            _w.SetFooterIntakeProgress(
                stageText,
                scannedCount,
                totalCount,
                acceptedCount,
                hasKnownTotal);
        }

        public void UpdateUiState()
        {
            _w.UpdateUiState();
        }

        public void RequestSelectFirstQueueRowIfNone()
        {
            if (_w._queueView.Count <= 0 || _w.TasksDataGrid.SelectedItem is not null)
            {
                return;
            }

            _w.TasksDataGrid.SelectedIndex = 0;
            _w._viewModel.SelectedTask = _w.TasksDataGrid.SelectedItem as TaskQueueItemViewModel;
        }

        public void OnQueueClearedUi()
        {
            _w._executionLogLines.Clear();
            _w._loggedExecutionSignatures.Clear();

            SetFooterStatus(MainWindowMessages.QueueClearedFooter);
        }

        public void ShowError(string titleResourceKey, string messageBody)
        {
            _w.ShowNoticeDialog(titleResourceKey, messageBody);
        }

        public void OpenExplorerForSelectedQueueItem(TaskQueueItemViewModel? item)
        {
            _w.OpenFolderForQueueItem(item);
        }

        public Task OpenOperationLogForQueueItemAsync(TaskQueueItemViewModel? item)
        {
            return _w.OpenOperationLogForQueueItemAsync(item);
        }

        public bool CanOpenOperationLogTarget(TaskQueueItemViewModel? item)
        {
            return _w.TryGetQueueItemOperationLogTarget(item, out _);
        }

        public Task RunRedumpIntegrityForSelectedQueueItemAsync(TaskQueueItemViewModel? item)
        {
            return _w.RunRedumpIntegrityForSelectedQueueItemAsync(item);
        }

        public bool CanRunRedumpIntegrityForSelectedQueueItem(TaskQueueItemViewModel? item)
        {
            return _w.CanRunRedumpIntegrityForSelectedQueueItem(item);
        }

        public Task RunRedumpIntegrityForAllQueueItemsAsync()
        {
            return _w.RunRedumpIntegrityForAllQueueItemsAsync();
        }

        public bool CanRunRedumpIntegrityForAnyQueueItem()
        {
            return _w.CanRunRedumpIntegrityForAnyQueueItem();
        }

        public Task ShowRedumpDetails(TaskQueueItemViewModel? item)
        {
            return _w.ShowRedumpDetails(item);
        }

        public Task ApplyRedumpSuggestedNameAsync(TaskQueueItemViewModel? item)
        {
            return _w.ApplyRedumpSuggestedNameAsync(item);
        }

        public bool CanApplyRedumpSuggestedName(TaskQueueItemViewModel? item)
        {
            return _w.CanApplyRedumpSuggestedName(item);
        }

        public bool CanOpenExplorerTarget(TaskQueueItemViewModel? item)
        {
            return _w.TryGetQueueItemExplorerTarget(item, out _);
        }

        public void RemoveQueueItem(TaskQueueItemViewModel? item)
        {
            _w.RemoveQueueItemFromSession(item);
        }

        public void RetryQueueItem(TaskQueueItemViewModel? item)
        {
            _w.RetryQueueItemFromSession(item);
        }

        public void CancelQueueJob(TaskQueueItemViewModel? item)
        {
            _w.CancelQueueJobFromSession(item);
        }

        public void OpenOptions()
        {
            _w.OpenOptionsDialog();
        }

        public void OpenAbout()
        {
            _w.OpenAboutDialog();
        }
    }
}