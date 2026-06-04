using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.UiPorts.Shell;

public interface IApplicationShellStateProvider
{
    ValueTask<ApplicationShellStateSnapshot> GetCurrentStateAsync(CancellationToken cancellationToken = default);
}
