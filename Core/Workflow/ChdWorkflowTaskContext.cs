using HakamiqChdTool.App.Core.Queue;
using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Core.Workflow;

public sealed class ChdWorkflowTaskContext
{
    private QueueItemSnapshot? _snapshot;
    private IQueueItemStateSink? _sink;
    private AppSettings? _settings;
    private Func<string>? _getChdmanPath;

    public required QueueItemSnapshot Snapshot
    {
        get => _snapshot ?? throw new InvalidOperationException("Workflow snapshot was not initialized.");
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _snapshot = value;
        }
    }

    public required IQueueItemStateSink Sink
    {
        get => _sink ?? throw new InvalidOperationException("Workflow sink was not initialized.");
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _sink = value;
        }
    }

    public required AppSettings Settings
    {
        get => _settings ?? throw new InvalidOperationException("Workflow settings were not initialized.");
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _settings = value;
        }
    }

    public required Func<string> GetChdmanPath
    {
        get => _getChdmanPath ?? throw new InvalidOperationException("Workflow chdman path resolver was not initialized.");
        init
        {
            ArgumentNullException.ThrowIfNull(value);
            _getChdmanPath = value;
        }
    }

    public Func<PremiumFeature, bool> CanUsePremiumFeature { get; init; } = static _ => false;

    public ChdWorkflowMode Mode { get; init; } = ChdWorkflowMode.ProcessQueueItem;

    public Action? OnUiRefresh { get; init; }
}