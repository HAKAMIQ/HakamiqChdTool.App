using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.UiPorts.Pickers;

public interface IFolderPickerService
{
    ValueTask<string?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default);
}
