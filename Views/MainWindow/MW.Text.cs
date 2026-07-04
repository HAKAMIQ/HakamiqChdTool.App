using HakamiqChdTool.App.Core.Session;
using HakamiqChdTool.App.Core.Workflow.Paths;
using HakamiqChdTool.App.Localization;
using HakamiqChdTool.App.Models;
using HakamiqChdTool.App.Ui.Queue;
using HakamiqChdTool.App.Services;
using HakamiqChdTool.App.Services.Features;
using HakamiqChdTool.App.Services.M3u;
using HakamiqChdTool.App.Services.StorageAdvisor;
using HakamiqChdTool.App.ViewModels;
using HakamiqChdTool.App.ViewModels.Virtualization;
using HakamiqChdTool.App.Views;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace HakamiqChdTool.App;

public partial class MainWindow
{

    private static string ResolveDialogText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return ArabicUi.ResolveDisplayString(value.Trim());
    }
}
