using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using HakamiqChdTool.App.Ui.WpfAdapters;

namespace HakamiqChdTool.App.Ui.Shell;

// Window chrome for the app's custom-drawn (WindowChrome) windows. Every call is
// best-effort: unsupported attributes fail silently and older Windows builds keep
// the WPF-drawn chrome unchanged.
internal static class WindowBackdrop
{
    private const int WmGetMinMaxInfo = 0x0024;
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

        if (isMainWindow)
        {
            HwndSource.FromHwnd(handle)?.AddHook(MainWindowHook);
        }

        if (!AttachedWindows.TryGetValue(window, out DesignedLimits? limits))
        {
            return;
        }

        limits.MinWidth = window.MinWidth;
        limits.MinHeight = window.MinHeight;
        limits.MaxHeight = window.MaxHeight;

        FitToWorkArea(window, handle, limits, shrinkSize: true);
        window.DpiChanged += (_, _) => FitToWorkArea(window, handle, limits, shrinkSize: true);
        window.LocationChanged += (_, _) => FitToWorkArea(window, handle, limits, shrinkSize: false);

        // The startup location is computed from the unclamped size, so a clamped window
        // can start partly off-screen. Pull it back once it is placed and rendered.
        void ContentRendered(object? sender, EventArgs e)
        {
            window.ContentRendered -= ContentRendered;
            KeepOnWorkArea(window, handle);
        }

        window.ContentRendered += ContentRendered;
    }

    private static void KeepOnWorkArea(Window window, nint handle)
    {
        if (window.WindowState != WindowState.Normal ||
            !TryGetWorkArea(handle, out Rect workArea) ||
            !GetWindowRect(handle, out NativeRect deviceBounds))
        {
            return;
        }

        // Window.Left/Top are NaN for centered windows, so use the actual bounds.
        Rect bounds = ToDips(handle, deviceBounds);

        double left = Math.Max(Math.Min(bounds.Left, workArea.Right - bounds.Width), workArea.Left);
        double top = Math.Max(Math.Min(bounds.Top, workArea.Bottom - bounds.Height), workArea.Top);

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

    // Keeps the designed minimum sizes and preferred sizes from exceeding the current
    // monitor, so footers and action buttons stay reachable at high scale factors.
    // Limits are recomputed from the designed values, so they recover on larger monitors.
    private static void FitToWorkArea(Window window, nint handle, DesignedLimits limits, bool shrinkSize)
    {
        if (!TryGetWorkArea(handle, out Rect workArea))
        {
            return;
        }

        SetIfChanged(window, FrameworkElement.MinWidthProperty, Math.Min(limits.MinWidth, workArea.Width));
        SetIfChanged(window, FrameworkElement.MinHeightProperty, Math.Min(limits.MinHeight, workArea.Height));
        SetIfChanged(window, FrameworkElement.MaxHeightProperty, Math.Min(limits.MaxHeight, workArea.Height));

        if (!shrinkSize)
        {
            return;
        }

        bool sizesWidthToContent =
            window.SizeToContent is SizeToContent.Width or SizeToContent.WidthAndHeight;

        bool sizesHeightToContent =
            window.SizeToContent is SizeToContent.Height or SizeToContent.WidthAndHeight;

        if (!sizesWidthToContent && window.Width > workArea.Width)
        {
            window.Width = workArea.Width;
        }

        if (!sizesHeightToContent && window.Height > workArea.Height)
        {
            window.Height = workArea.Height;
        }
    }

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
    private static nint MainWindowHook(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo && lParam != 0 && TryGetMonitorInfo(hwnd, out MonitorInfo info))
        {
            MinMaxInfo minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);

            minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            minMax.MaxSize.X = info.Work.Right - info.Work.Left;
            minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;

            Marshal.StructureToPtr(minMax, lParam, false);
        }

        return 0;
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

        public double MaxHeight { get; set; } = double.PositiveInfinity;
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
