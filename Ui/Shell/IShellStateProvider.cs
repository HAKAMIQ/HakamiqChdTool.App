using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Ui.Shell;

public interface IShellStateProvider
{
    ValueTask<ShellState> GetCurrentStateAsync(CancellationToken cancellationToken = default);
}
