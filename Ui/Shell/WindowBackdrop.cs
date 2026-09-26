using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using HakamiqChdTool.App.Ui.WpfAdapters;

namespace HakamiqChdTool.App.Ui.Shell;

// Window chrome for the app's custom-drawn (WindowChrome) windows. Every call is
// best-effort: unsupported attributes fail silently and older Windows builds keep
// the WPF-drawn chrome unchanged.
internal static class WindowBackdrop
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmEnterSizeMove = 0x0231;
    private const int WmExitSizeMove = 0x0232;
    private const int ImmersiveDarkModeAttribute = 20;
    private const int WindowCornerPreferenceAttribute = 33;
    private const int RoundCornerPreference = 2;
    private const uint MonitorDefaultToNearest = 2;

    // One entry per window: guards against double attachment and keeps the designed
    // size limits so they can be restored when the window moves to a larger monitor.
    private static readonly ConditionalWeakTable<Window, DesignedLimits> AttachedWindows = new();

    internal static void ApplyMainWindow(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Attach(window, isMainWindow: true);
    }

    internal static void ApplyDialog(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        Attach(window, isMainWindow: false);
    }

    private static void Attach(Window window, bool isMainWindow)
    {
        if (!AttachedWindows.TryAdd(window, new DesignedLimits()))
        {
            return;
        }

        nint handle = new WindowInteropHelper(window).Handle;

        if (handle != 0)
        {
            OnSourceReady(window, handle, isMainWindow);
            return;
        }

        void SourceInitialized(object? sender, EventArgs e)
        {
            window.SourceInitialized -= SourceInitialized;

            nint initializedHandle = new WindowInteropHelper(window).Handle;

            if (initializedHandle != 0)
            {
                OnSourceReady(window, initializedHandle, isMainWindow);
            }
        }

        window.SourceInitialized += SourceInitialized;
    }

    private static void OnSourceReady(Window window, nint handle, bool isMainWindow)
    {
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            SetDwmAttribute(handle, WindowCornerPreferenceAttribute, RoundCornerPreference);
        }

        ApplyDarkMode(handle);

        void ThemeChanged(object? sender, EventArgs e) => ApplyDarkMode(handle);

        ThemeService.Instance.ThemeChanged += ThemeChanged;
        window.Closed += (_, _) => ThemeService.Instance.ThemeChanged -= ThemeChanged;

        if (!AttachedWindows.TryGetValue(window, out DesignedLimits? limits))
        {
            return;
        }

        limits.MinWidth = window.MinWidth;
        limits.MinHeight = window.MinHeight;
        limits.MaxWidth = window.MaxWidth;
        limits.MaxHeight = window.MaxHeight;
        limits.Monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        // One hook per window: tracks the move/size loop and, for the main window, pins
        // the maximized rectangle. It is released with the window's HwndSource.
        HwndSource.FromHwnd(handle)?.AddHook(
            (nint hwnd, int msg, nint wParam, nint lParam, ref bool handled) =>
                WindowHook(window, limits, isMainWindow, hwnd, msg, lParam));

        FitInitialSize(window, handle, limits);

        window.DpiChanged += (_, _) => OnMonitorChanged(window, handle, limits);
        window.LocationChanged += (_, _) =>
        {
            // Moving within the same monitor needs nothing, so a drag cannot jitter.
            if (MonitorFromWindow(handle, MonitorDefaultToNearest) != limits.Monitor)
            {
                OnMonitorChanged(window, handle, limits);
            }
        };

        // The startup location is computed from the unclamped size, so a clamped window
        // can start partly off-screen. Pull it back once it is placed and rendered.
        void ContentRendered(object? sender, EventArgs e)
        {
            window.ContentRendered -= ContentRendered;
            ConstrainToWorkArea(window, handle, limits);
        }

        window.ContentRendered += ContentRendered;
    }

    private static nint WindowHook(
        Window window,
        DesignedLimits limits,
        bool isMainWindow,
        nint hwnd,
        int msg,
        nint lParam)
    {
        switch (msg)
        {
            case WmGetMinMaxInfo when isMainWindow:
                PinMaximizedBounds(hwnd, lParam);
                break;

            case WmEnterSizeMove:
                limits.InMoveLoop = true;
                break;

            case WmExitSizeMove:
                limits.InMoveLoop = false;

                if (limits.FitPending)
                {
                    limits.FitPending = false;
                    ScheduleConstrain(window, hwnd, limits);
                }

                break;
        }

        return 0;
    }

    // The window reached another monitor (or its DPI changed). While the user is still
    // dragging, wait for the move loop to end instead of resizing under the cursor.
    private static void OnMonitorChanged(Window window, nint handle, DesignedLimits limits)
    {
        limits.Monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        if (limits.InMoveLoop)
        {
            limits.FitPending = true;
            return;
        }

        ScheduleConstrain(window, handle, limits);
    }

    // Deferred so it never runs inside LocationChanged/DpiChanged; the resulting move
    // stays on the same monitor and therefore does not schedule another pass.
    private static void ScheduleConstrain(Window window, nint handle, DesignedLimits limits)
    {
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() => ConstrainToWorkArea(window, handle, limits)));
    }

    private static void ConstrainToWorkArea(Window window, nint handle, DesignedLimits limits)
    {
        if (PresentationSource.FromVisual(window) is null ||
            !TryGetWorkArea(handle, out Rect workArea) ||
            !GetWindowRect(handle, out NativeRect deviceBounds))
        {
            return;
        }

        limits.Monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        // Window.Left/Top are NaN for centered windows, so use the actual bounds.
        ApplyWorkArea(window, limits, workArea, ToDips(handle, deviceBounds));
    }

    // Test seam: applies an explicit work area (in DIPs) to an attached window, exactly as
    // a monitor change would, so work-area transitions can be exercised on one monitor.
    internal static void ConstrainToWorkArea(Window window, Rect workArea)
    {
        nint handle = new WindowInteropHelper(window).Handle;

        if (handle != 0 &&
            AttachedWindows.TryGetValue(window, out DesignedLimits? limits) &&
            GetWindowRect(handle, out NativeRect deviceBounds))
        {
            ApplyWorkArea(window, limits, workArea, ToDips(handle, deviceBounds));
        }
    }

    // Recomputes the limits from the designed values, so they recover on larger monitors.
    // A normal window is then shrunk to the work area and moved fully inside it; maximized
    // and minimized windows are sized by the system and only get their limits updated.
    private static void ApplyWorkArea(Window window, DesignedLimits limits, Rect workArea, Rect bounds)
    {
        ApplyLimits(window, limits, workArea);

        if (window.WindowState != WindowState.Normal)
        {
            return;
        }

        double width = bounds.Width;
        double height = bounds.Height;

        if (width > workArea.Width)
        {
            width = workArea.Width;

            if (!SizesWidthToContent(window))
            {
                window.Width = width;
            }
        }

        if (height > workArea.Height)
        {
            height = workArea.Height;

            if (!SizesHeightToContent(window))
            {
                window.Height = height;
            }
        }

        double left = Math.Max(Math.Min(bounds.Left, workArea.Right - width), workArea.Left);
        double top = Math.Max(Math.Min(bounds.Top, workArea.Bottom - height), workArea.Top);

        if (Math.Abs(left - bounds.Left) >= 1 || Math.Abs(top - bounds.Top) >= 1)
        {
            window.Left = left;
            window.Top = top;
        }
    }

    private static Rect ToDips(nint handle, NativeRect deviceRect)
    {
        Matrix fromDevice =
            HwndSource.FromHwnd(handle)?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;

        return new Rect(
            fromDevice.Transform(new Point(deviceRect.Left, deviceRect.Top)),
            fromDevice.Transform(new Point(deviceRect.Right, deviceRect.Bottom)));
    }

    private static void ApplyDarkMode(nint handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 18985))
        {
            return;
        }

        SetDwmAttribute(
            handle,
            ImmersiveDarkModeAttribute,
            ThemeService.Instance.IsDarkTheme ? 1 : 0);
    }

    // Before the first layout there are no real bounds yet, so only the designed sizes are
    // clamped; ContentRendered then places the window inside the work area.
    private static void FitInitialSize(Window window, nint handle, DesignedLimits limits)
    {
        if (!TryGetWorkArea(handle, out Rect workArea))
        {
            return;
        }

        ApplyLimits(window, limits, workArea);

        if (!SizesWidthToContent(window) && window.Width > workArea.Width)
        {
            window.Width = workArea.Width;
        }

        if (!SizesHeightToContent(window) && window.Height > workArea.Height)
        {
            window.Height = workArea.Height;
        }
    }

    // Keeps the designed minimum sizes from exceeding the monitor, so footers and action
    // buttons stay reachable at high scale factors. Maximums are only capped where a
    // window has a designed maximum or sizes itself to content (WPF ignores Width/Height
    // changes on those, but honors MaxWidth/MaxHeight). A manually sized window with no
    // designed maximum stays unbounded, so maximizing on a larger monitor is never clipped.
    private static void ApplyLimits(Window window, DesignedLimits limits, Rect workArea)
    {
        SetIfChanged(window, FrameworkElement.MinWidthProperty, Math.Min(limits.MinWidth, workArea.Width));
        SetIfChanged(window, FrameworkElement.MinHeightProperty, Math.Min(limits.MinHeight, workArea.Height));

        bool sizesToContent = window.SizeToContent != SizeToContent.Manual;

        if (!double.IsPositiveInfinity(limits.MaxWidth) || sizesToContent)
        {
            SetIfChanged(window, FrameworkElement.MaxWidthProperty, Math.Min(limits.MaxWidth, workArea.Width));
        }

        if (!double.IsPositiveInfinity(limits.MaxHeight) || sizesToContent)
        {
            SetIfChanged(window, FrameworkElement.MaxHeightProperty, Math.Min(limits.MaxHeight, workArea.Height));
        }
    }

    private static bool SizesWidthToContent(Window window) =>
        window.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight;

    private static bool SizesHeightToContent(Window window) =>
        window.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight;

    private static void SetIfChanged(Window window, DependencyProperty property, double value)
    {
        if (!((double)window.GetValue(property)).Equals(value))
        {
            window.SetValue(property, value);
        }
    }

    private static bool TryGetWorkArea(nint handle, out Rect workArea)
    {
        workArea = Rect.Empty;

        if (!TryGetMonitorInfo(handle, out MonitorInfo info))
        {
            return false;
        }

        workArea = ToDips(handle, info.Work);

        return workArea.Width > 0 && workArea.Height > 0;
    }

    // A WindowChrome window maximizes to the monitor bounds plus the hidden resize
    // frame, which clips the header and footer edges. Pin the maximized rectangle to
    // the work area of the monitor the window is on.
    // Position is relative to the monitor origin, so monitors left of or above the
    // primary (negative coordinates) are handled on both axes.
    private static void PinMaximizedBounds(nint hwnd, nint lParam)
    {
        if (lParam == 0 || !TryGetMonitorInfo(hwnd, out MonitorInfo info))
        {
            return;
        }

        MinMaxInfo minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);

        minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
        minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
        minMax.MaxSize.X = info.Work.Right - info.Work.Left;
        minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;

        Marshal.StructureToPtr(minMax, lParam, false);
    }

    private static bool TryGetMonitorInfo(nint handle, out MonitorInfo info)
    {
        info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };

        nint monitor = MonitorFromWindow(handle, MonitorDefaultToNearest);

        return monitor != 0 && GetMonitorInfo(monitor, ref info);
    }

    private static void SetDwmAttribute(nint handle, int attribute, int value)
    {
        _ = DwmSetWindowAttribute(handle, attribute, ref value, sizeof(int));
    }

    private sealed class DesignedLimits
    {
        public double MinWidth { get; set; }

        public double MinHeight { get; set; }

        public double MaxWidth { get; set; } = double.PositiveInfinity;

        public double MaxHeight { get; set; } = double.PositiveInfinity;

        public nint Monitor { get; set; }

        public bool InMoveLoop { get; set; }

        public bool FitPending { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern nint MonitorFromWindow(nint hwnd, uint flags);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
}
