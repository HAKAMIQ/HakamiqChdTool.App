using System;
using System.IO;
using System.Threading;

namespace HakamiqChdTool.App;

internal static class SingleInstanceGate
{
    internal const string MutexName = @"Local\HakamiqChdTool.App.SingleInstance";

    internal static bool TryAcquire(out Mutex? mutex, out Exception? error)
    {
        mutex = null;
        error = null;
        return TryAcquire(MutexName, out mutex, out error);
    }

    internal static bool TryAcquire(string name, out Mutex? mutex, out Exception? error)
    {
        mutex = null;
        error = null;
        try
        {
            var candidate = new Mutex(initiallyOwned: true, name: name, createdNew: out bool createdNew);
            if (!createdNew)
            {
                candidate.Dispose();
                return false;
            }

            mutex = candidate;
            return true;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
    }
}
