using HakamiqChdTool.App.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace HakamiqChdTool.App.Core.Contracts;

public interface IChdVerificationService
{
    Task<ChdVerificationResult> VerifyAsync(
        string chdmanPath,
        string chdFilePath,
        IProgress<int>? progress = null,
        Action<int>? onProcessStarted = null,
        CancellationToken cancellationToken = default,
        ChdmanProcessPriorityMode priorityMode = ChdmanProcessPriorityMode.Quiet);
}
