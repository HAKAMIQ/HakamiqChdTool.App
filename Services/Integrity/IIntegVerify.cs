using System.Threading;

namespace HakamiqChdTool.App.Services.Integrity;

public interface IIntegrityVerifier
{
    IntegrityVerificationResult VerifyFile(
        string filePath,
        CancellationToken cancellationToken = default);
}