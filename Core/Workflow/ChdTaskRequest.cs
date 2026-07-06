using System;
using HakamiqChdTool.App.Models;

namespace HakamiqChdTool.App.Core.Workflow;

public class ChdTaskRequest
{
    public Guid OperationId { get; set; } = Guid.NewGuid();

    public string InputPath { get; set; } = string.Empty;

    public bool IsArchive { get; set; }

    public bool Verify { get; set; }

    public object? Options { get; set; }

    public Action<double>? OnProgress { get; set; }

    public Action<ProgressEvent>? OnProgressEvent { get; set; }
}
