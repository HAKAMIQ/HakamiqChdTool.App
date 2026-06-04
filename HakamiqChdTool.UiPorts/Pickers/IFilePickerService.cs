using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.UiPorts.Pickers;

public interface IFilePickerService
{
    ValueTask<IReadOnlyList<string>> PickFilesAsync(FilePickerRequest request, CancellationToken cancellationToken = default);
}
