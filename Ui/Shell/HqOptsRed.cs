using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.ViewModels;
using Microsoft.Win32;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App.Ui.Shell;

internal sealed partial class HqOptionsShell
{
    public void ImportRedumpDatabase(object sender, RoutedEventArgs e)
    {
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

            // The system name box is hidden, so a value left in an old settings file must not silently
            // re-label every import; the DAT file name is what the user picked and can see.
            string systemName = Path.GetFileNameWithoutExtension(dialog.FileName);

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