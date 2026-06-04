using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using System;

namespace HakamiqChdTool.App.Core.Queue;

public sealed class ChdQueueItem
{
    public const string DefaultStatus = TaskQueueStateCodes.Pending;
    public const string DefaultMode = "Convert";

    private string _inputPath = string.Empty;
    private string _status = DefaultStatus;
    private double _progress;
    private string _mode = DefaultMode;

    public Guid Id { get; init; } = Guid.NewGuid();

    public string InputPath
    {
        get => _inputPath;
        set => _inputPath = value ?? string.Empty;
    }

    public string Status
    {
        get => _status;
        set => _status = string.IsNullOrWhiteSpace(value) ? DefaultStatus : value.Trim();
    }

    public double Progress
    {
        get => _progress;
        set => _progress = double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0;
    }

    public string? OutputPath { get; set; }

    public string? Error { get; set; }

    public string Mode
    {
        get => _mode;
        set => _mode = string.IsNullOrWhiteSpace(value) ? DefaultMode : value.Trim();
    }

    public QueueExecutionProfile ExecutionProfile { get; set; } = QueueExecutionProfile.Standard;

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(InputPath)
            ? Id.ToString()
            : InputPath;
    }
}