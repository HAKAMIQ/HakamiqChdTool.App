using Ookii.Dialogs.Wpf;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HakamiqChdTool.App.Ui.Shell;

public sealed class FolderPickerService : IFolderPickerService
{
    public ValueTask<string?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new VistaFolderBrowserDialog
        {
            Description = request.Title,
            UseDescriptionForTitle = true,
            ShowNewFolderButton = request.ShowNewFolderButton,
            SelectedPath = Directory.Exists(request.SelectedPath) ? request.SelectedPath : null
        };

        Window? owner = OwnerResolver.GetActiveOwner();
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return ValueTask.FromResult<string?>(result == true ? dialog.SelectedPath ?? string.Empty : null);
    }
}
