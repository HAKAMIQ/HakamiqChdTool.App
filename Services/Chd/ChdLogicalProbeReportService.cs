using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services.Chd;

public static class ChdLogicalProbeReportService
{
	public static Task<ChdLogicalProbeResult> ProbeAsync(string path, CancellationToken cancellationToken)
	{
		var service = new ChdLogicalProbeService();
		return service.ProbeAsync(path, cancellationToken);
	}
}