using System;
using System.Threading;

namespace HakamiqChdTool.App.Core.Workflow;

/// <summary>
/// Counts workflows currently executing in this process. Automatic orphan cleanup consults it so
/// that one job never classifies the workspace of a sibling job as abandoned.
/// </summary>
internal static class ActiveWorkflowRegistry
{
    private static int _active;

    public static int ActiveCount => Volatile.Read(ref _active);

    public static IDisposable Enter()
    {
        Interlocked.Increment(ref _active);
        return new Lease();
    }

    private sealed class Lease : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                Interlocked.Decrement(ref _active);
            }
        }
    }
}
