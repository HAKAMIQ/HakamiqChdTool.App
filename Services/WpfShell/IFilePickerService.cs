using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.WpfShell;

public interface IFilePickerService
{
    ValueTask<IReadOnlyList<string>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default);
}
