namespace HakamiqChdTool.App.Services.Power;

internal interface IConversionPowerGuard : IDisposable
{
    void BeginCriticalConversionSession();

    void EndCriticalConversionSession();
}

