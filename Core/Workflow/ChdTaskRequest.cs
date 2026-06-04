using System;

namespace HakamiqChdTool.App.Core.Workflow;

public class ChdTaskRequest
{
    public string InputPath { get; set; } = string.Empty;

    public bool IsArchive { get; set; }

    public bool Verify { get; set; }

    public object? Options { get; set; }

    public Action<double>? OnProgress { get; set; }
}