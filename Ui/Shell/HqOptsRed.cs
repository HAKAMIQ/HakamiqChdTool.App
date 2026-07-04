using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.ViewModels;
using Microsoft.Win32;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
        public void ImportRedumpDatabase(object sender, RoutedEventArgs e)
        {
            if (!RequireAppFeature(AppFeature.RedumpDatabaseImport))
            {
                return;
            }

            _ = ImportRedumpDatabaseAsync();
        }

        private async Task ImportRedumpDatabaseAsync()
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = ResolveUiText(SelectRedumpDatTitleKey),
                    Filter = ResolveUiText(RedumpDatFilterKey)
                };

                string prior = _owner.ViewModel.RedumpDatXmlPath.Trim();
                if (File.Exists(prior))
                {
                    dialog.FileName = Path.GetFileName(prior);

                    string? directory = Path.GetDirectoryName(prior);
                    if (!string.IsNullOrWhiteSpace(directory))
                    {
                        dialog.InitialDirectory = directory;
                    }
                }

                if (dialog.ShowDialog(_owner) != true)
                {
                    return;
                }

                _owner.ViewModel.RedumpDatXmlPath = dialog.FileName;

                string systemName = string.IsNullOrWhiteSpace(_owner.ViewModel.RedumpSystemName)
                    ? Path.GetFileNameWithoutExtension(dialog.FileName)
                    : _owner.ViewModel.RedumpSystemName.Trim();

                _owner.RedumpPanelView.ImportRedumpDatabaseButtonView.IsEnabled = false;
                _owner.RedumpPanelView.RedumpImportProgressBarView.Visibility = Visibility.Visible;
                _owner.RedumpPanelView.RedumpImportProgressBarView.IsIndeterminate = true;
                _owner.RedumpPanelView.RedumpImportStatusTextBlockView.Visibility = Visibility.Visible;
                _owner.RedumpPanelView.RedumpImportStatusTextBlockView.Text = ResolveUiText(RedumpImportRunningKey);

                CancellationToken operationToken = _windowLifetimeCts.Token;

                try
                {
                    var progress = new Progress<RedumpImportProgress>(progressValue =>
                    {
                        if (_isClosed || operationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        _owner.RedumpPanelView.RedumpImportStatusTextBlockView.Text =
                            ResolveUiText(RedumpImportRowsProcessedKey, progressValue.RowsInserted);
                    });

                    RedumpImportResult result = await RedumpSqliteManager.Default
                        .ImportDatFileAsync(dialog.FileName, systemName, progress, operationToken)
                        .ConfigureAwait(true);

                    if (_isClosed || operationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    string resultMessage = ResolveUiText(result.MessageKey, result.MessageArgs);
                    _owner.RedumpPanelView.RedumpImportStatusTextBlockView.Text = resultMessage;

                    ShowNoticeDialog(
                        result.Success ? RedumpImportSuccessTitleKey : RedumpImportFailedTitleKey,
                        resultMessage);

                    QueueDatabaseStateRefresh();
                }
                catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (!_isClosed)
                    {
                        ShowNoticeDialog(
                            RedumpImportErrorTitleKey,
                            RuntimeDiagnosticFormatter.SummarizeException(ex));
                    }
                }
                finally
                {
                    if (!_isClosed)
                    {
                        _owner.RedumpPanelView.ImportRedumpDatabaseButtonView.IsEnabled = true;
                        _owner.RedumpPanelView.RedumpImportProgressBarView.IsIndeterminate = false;
                        _owner.RedumpPanelView.RedumpImportProgressBarView.Visibility = Visibility.Collapsed;
                        _owner.RedumpPanelView.RedumpImportStatusTextBlockView.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNoticeDialog(
                    OperationErrorTitleKey,
                    RuntimeDiagnosticFormatter.SummarizeException(ex));
            }
        }

        public void DownloadDatabase(object sender, RoutedEventArgs e)
        {
            if (!RequireAppFeature(AppFeature.RedumpDatabaseImport))
            {
                return;
            }

            _ = DownloadDatabaseAsync();
        }

        private async Task DownloadDatabaseAsync()
        {
            try
            {
                _owner.ViewModel.ValidateForSave();

                if (!_owner.ViewModel.CanDownloadSelectedRedumpDatabase)
                {
                    ShowNoticeDialog(MissingDataTitleKey, RedumpDownloadUrlRequiredKey);
                    return;
                }

                if (_owner.ViewModel.GetErrors(nameof(OptionsViewModel.RedumpDatabaseDownloadUrl)).Cast<object>().Any())
                {
                    ShowNoticeDialog(MissingDataTitleKey, RedumpDownloadInvalidUrlKey);
                    return;
                }

                _owner.RedumpPanelView.DownloadDatabaseButtonView.IsEnabled = false;
                _owner.RedumpPanelView.DatabaseDownloadProgressBarView.Visibility = Visibility.Visible;
                _owner.RedumpPanelView.DatabaseDownloadStatusTextBlockView.Visibility = Visibility.Visible;
                _owner.RedumpPanelView.DatabaseDownloadProgressBarView.Value = 0;
                _owner.RedumpPanelView.DatabaseDownloadStatusTextBlockView.Text = ResolveUiText(RedumpDownloadRunningKey);

                CancellationToken operationToken = _windowLifetimeCts.Token;

                try
                {
                    var progress = new Progress<RedumpGitHubSyncProgress>(progressValue =>
                    {
                        if (_isClosed || operationToken.IsCancellationRequested)
                        {
                            return;
                        }

                        _owner.RedumpPanelView.DatabaseDownloadProgressBarView.Value = progressValue.Percent;
                        _owner.RedumpPanelView.DatabaseDownloadStatusTextBlockView.Text =
                            ResolveUiText(progressValue.MessageKey, progressValue.MessageArgs);
                    });

                    RedumpGitHubSyncResult sync = await _syncManager
                        .SyncFromGitHubAsync(_owner.ViewModel.RedumpDatabaseDownloadUrl.Trim(), progress, operationToken)
                        .ConfigureAwait(true);

                    if (_isClosed || operationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    string syncMessage = ResolveUiText(sync.MessageKey, sync.MessageArgs);

                    if (!sync.Success)
                    {
                        _owner.RedumpPanelView.DatabaseDownloadStatusTextBlockView.Text = syncMessage;
                        ShowNoticeDialog(RedumpSyncFailedTitleKey, syncMessage);
                        return;
                    }

                    _owner.ViewModel.SetDatabaseLastSyncedUtc(sync.SyncedAtUtc.ToString("O", CultureInfo.InvariantCulture));
                    QueueDatabaseStateRefresh();

                    _owner.RedumpPanelView.DatabaseDownloadStatusTextBlockView.Text = syncMessage;
                }
                catch (OperationCanceledException) when (operationToken.IsCancellationRequested)
                {
                }
                catch (Exception ex)
                {
                    if (!_isClosed)
                    {
                        _owner.RedumpPanelView.DatabaseDownloadStatusTextBlockView.Text = ResolveUiText(RedumpDownloadFailedKey);

                        ShowNoticeDialog(
                            RedumpDownloadErrorTitleKey,
                            RuntimeDiagnosticFormatter.SummarizeException(ex));
                    }
                }
                finally
                {
                    if (!_isClosed)
                    {
                        _owner.RedumpPanelView.DownloadDatabaseButtonView.ClearValue(UIElement.IsEnabledProperty);
                        _owner.RedumpPanelView.DatabaseDownloadProgressBarView.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch (Exception ex)
            {
                ShowNoticeDialog(
                    OperationErrorTitleKey,
                    RuntimeDiagnosticFormatter.SummarizeException(ex));
            }
        }

        private void QueueDatabaseStateRefresh()
        {
            if (_isClosed)
            {
                return;
            }

            if (!_owner.Dispatcher.CheckAccess())
            {
                _ = _owner.Dispatcher.BeginInvoke(new Action(QueueDatabaseStateRefresh), DispatcherPriority.ContextIdle);
                return;
            }

            _databaseStateRefreshCts?.Cancel();
            _databaseStateRefreshCts?.Dispose();
            _databaseStateRefreshCts = CancellationTokenSource.CreateLinkedTokenSource(_windowLifetimeCts.Token);

            int generation = Interlocked.Increment(ref _databaseStateRefreshGeneration);
            CancellationToken cancellationToken = _databaseStateRefreshCts.Token;

            _owner.ViewModel.IsDatabaseAvailable = false;
            _owner.ViewModel.DatabaseStatusText = ResolveUiText(DatabaseCheckingKey);

            _ = RefreshDatabaseStateAsync(generation, cancellationToken);
        }

        private async Task RefreshDatabaseStateAsync(int generation, CancellationToken cancellationToken)
        {
            try
            {
                bool isAvailable = await Task.Run(LoadDatabaseAvailability, cancellationToken).ConfigureAwait(true);

                if (_isClosed
                    || cancellationToken.IsCancellationRequested
                    || generation != Volatile.Read(ref _databaseStateRefreshGeneration))
                {
                    return;
                }

                _owner.ViewModel.IsDatabaseAvailable = isAvailable;
                _owner.ViewModel.DatabaseStatusText = ResolveUiText(isAvailable ? DatabaseAvailableKey : DatabaseMissingKey);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                if (_isClosed
                    || cancellationToken.IsCancellationRequested
                    || generation != Volatile.Read(ref _databaseStateRefreshGeneration))
                {
                    return;
                }

                Logger.Warning(ex, "Failed to refresh Redump SQLite database state from the advanced options window.");

                _owner.ViewModel.IsDatabaseAvailable = false;
                _owner.ViewModel.DatabaseStatusText = ResolveUiText(DatabaseReadFailedKey);
            }
        }

        private static bool LoadDatabaseAvailability()
        {
            RedumpSqliteManager database = RedumpSqliteManager.Default;
            database.EnsureInitialized();
            return database.HasAnyRows();
        }
}
