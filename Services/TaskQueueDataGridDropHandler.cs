using GongSolutions.Wpf.DragDrop;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace HakamiqChdTool.App.Services;

public sealed class TaskQueueDataGridDropHandler(
    Func<bool> isProcessing,
    Action<IReadOnlyList<string>> enqueuePaths) : IDropTarget
{
    private readonly Func<bool> _isProcessing = isProcessing ?? throw new ArgumentNullException(nameof(isProcessing));
    private readonly Action<IReadOnlyList<string>> _enqueuePaths = enqueuePaths ?? throw new ArgumentNullException(nameof(enqueuePaths));

    public void DragOver(IDropInfo dropInfo)
    {
        ArgumentNullException.ThrowIfNull(dropInfo);

        if (_isProcessing())
        {
            dropInfo.Effects = DragDropEffects.None;
            dropInfo.NotHandled = false;
            return;
        }

        if (!TryGetFilePaths(dropInfo, out _))
        {
            dropInfo.Effects = DragDropEffects.None;
            dropInfo.NotHandled = true;
            return;
        }

        dropInfo.Effects = DragDropEffects.Copy;
        dropInfo.DropTargetAdorner = DropTargetAdorners.Highlight;
        dropInfo.NotHandled = false;
    }

    public void Drop(IDropInfo dropInfo)
    {
        ArgumentNullException.ThrowIfNull(dropInfo);

        if (_isProcessing())
        {
            return;
        }

        if (!TryGetFilePaths(dropInfo, out string[] rawPaths) || rawPaths.Length == 0)
        {
            return;
        }

        List<string> normalizedPaths = [];
        HashSet<string> returnedPaths = new(StringComparer.OrdinalIgnoreCase);

        foreach (string rawPath in rawPaths)
        {
            if (TaskQueueDropPathNormalizer.TryNormalizeRootPath(rawPath, out string? normalizedPath)
                && returnedPaths.Add(normalizedPath))
            {
                normalizedPaths.Add(normalizedPath);
            }
        }

        if (normalizedPaths.Count > 0)
        {
            _enqueuePaths(normalizedPaths);
        }
    }

    private static bool TryGetFilePaths(IDropInfo dropInfo, out string[] paths)
    {
        paths = [];

        try
        {
            if (dropInfo.Data is not IDataObject data)
            {
                return false;
            }

            if (!data.GetDataPresent(DataFormats.FileDrop, autoConvert: true))
            {
                return false;
            }

            if (data.GetData(DataFormats.FileDrop, autoConvert: true) is not string[] arr || arr.Length == 0)
            {
                return false;
            }

            paths = arr;
            return true;
        }
        catch (Exception ex) when (IsExpectedDragDropDataException(ex))
        {
            return false;
        }
    }

    private static bool IsExpectedDragDropDataException(Exception ex)
    {
        return ex is COMException
            or InvalidOperationException
            or ArgumentException
            or NotSupportedException;
    }
}