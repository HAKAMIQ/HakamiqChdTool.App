using HakamiqChdTool.App.Models;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Services;

public interface IChdmanCapabilityService
{
    Task<ChdmanCapabilitySnapshot> InspectAsync(string chdmanPath, CancellationToken cancellationToken);

    bool SupportsRequestedCompression(
        ChdmanCapabilitySnapshot capabilities,
        string command,
        string? resolvedCompression);

    bool SupportsRequestedHunkSize(
        ChdmanCapabilitySnapshot capabilities,
        string command,
        int hunkSizeBytes);
}
