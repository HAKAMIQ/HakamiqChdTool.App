using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Ui.Shell;

public interface IFolderPickerService
{
    ValueTask<string?> PickFolderAsync(FolderPickerRequest request, CancellationToken cancellationToken = default);
}
