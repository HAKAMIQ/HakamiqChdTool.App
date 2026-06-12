using HakamiqChdTool.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Contracts;

public interface IChdInfoService
{
    Task<ChdInfoResult> ReadInfoAsync(
        string chdmanPath,
        string chdFilePath,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default);
}
