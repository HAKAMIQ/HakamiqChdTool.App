using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.WpfShell;

public interface IApplicationShellStateProvider
{
    ValueTask<ApplicationShellStateSnapshot> GetCurrentStateAsync(CancellationToken cancellationToken = default);
}
