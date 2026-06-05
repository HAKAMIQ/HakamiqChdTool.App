using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace HakamiqChdTool.App.Services.WpfShell;

public sealed class WpfFilePickerService : IFilePickerService
{
    public ValueTask<IReadOnlyList<string>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var dialog = new OpenFileDialog
        {
            Title = request.Title,
            Filter = request.Filter,
            InitialDirectory = ResolveInitialDirectory(request.InitialDirectory),
            FileName = request.FileName ?? string.Empty,
            Multiselect = request.AllowMultiple
        };

        Window? owner = WpfOwnerResolver.GetActiveOwner();
        bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        if (result != true)
        {
            return ValueTask.FromResult<IReadOnlyList<string>>([]);
        }

        if (request.AllowMultiple)
        {
            return ValueTask.FromResult<IReadOnlyList<string>>(dialog.FileNames
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .ToArray());
        }

        return ValueTask.FromResult<IReadOnlyList<string>>(string.IsNullOrWhiteSpace(dialog.FileName) ? [] : [dialog.FileName]);
    }

    private static string ResolveInitialDirectory(string? initialDirectory)
    {
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            return initialDirectory;
        }

        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : AppContext.BaseDirectory;
    }
}
