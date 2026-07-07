using HakamiqChdTool.App.Models;
using System.Collections.Generic;

namespace HakamiqChdTool.App.Services;

public sealed record QueuePathImportResult(
    IReadOnlyList<string> SupportedPaths,
    IntakeBatchSummary Summary);