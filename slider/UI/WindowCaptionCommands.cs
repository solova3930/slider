using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;

namespace slider.UI;

/// <summary>Routes shared caption buttons through native window commands.</summary>
public static class WindowCaptionCommands
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(WindowCaptionCommands),
        new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);

    private static readonly DependencyPropertyKey MaximizedInsetsPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "MaximizedInsets", typeof(Thickness), typeof(WindowCaptionCommands), new PropertyMetadata(new Thickness()));
    public static readonly DependencyProperty MaximizedInsetsProperty = MaximizedInsetsPropertyKey.DependencyProperty;
    public static Thickness GetMaximizedInsets(DependencyObject target) => (Thickness)target.GetValue(MaximizedInsetsProperty);

    private static readonly CommandBinding[] Bindings =
    {
        new(SystemCommands.MinimizeWindowCommand, Minimize),
        new(SystemCommands.MaximizeWindowCommand, Maximize),
        new(SystemCommands.RestoreWindowCommand, Restore),
        new(SystemCommands.CloseWindowCommand, Close)
    };

    private static void OnIsEnabledChanged(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not Window window) return;
        if ((bool)args.NewValue)
        {
            window.StateChanged += UpdateInsets;
            window.SizeChanged += UpdateInsets;
            window.SourceInitialized += UpdateInsets;
            window.DpiChanged += UpdateInsets;
        }
        else
        {
            window.StateChanged -= UpdateInsets;
            window.SizeChanged -= UpdateInsets;
            window.SourceInitialized -= UpdateInsets;
            window.DpiChanged -= UpdateInsets;
            window.SetValue(MaximizedInsetsPropertyKey, new Thickness());
        }
        foreach (var binding in Bindings)
        {
            if ((bool)args.NewValue) window.CommandBindings.Add(binding);
            else window.CommandBindings.Remove(binding);
        }
    }

    private static void UpdateInsets(object? sender, EventArgs e)
    {
        if (sender is not Window window) return;
        var inset = new Thickness();
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            // Let DWM round the native frame and draw its shadow, without a light border.
            int cornerPreference = window.WindowStyle == WindowStyle.None ? 1 : 2;
            int borderColor = unchecked((int)0xFFFFFFFE); // DWMWA_COLOR_NONE
            DwmSetWindowAttribute(hwnd, 33, ref cornerPreference, sizeof(int));
            DwmSetWindowAttribute(hwnd, 34, ref borderColor, sizeof(int));
        }
        if (hwnd != IntPtr.Zero && window.WindowState == WindowState.Maximized && window.WindowStyle != WindowStyle.None)
        {
            var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (GetWindowRect(hwnd, out var rect) && GetMonitorInfo(MonitorFromWindow(hwnd, 2), ref monitor))
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                inset = new Thickness(
                    Math.Max(0, monitor.Work.Left - rect.Left) / dpi.DpiScaleX,
                    Math.Max(0, monitor.Work.Top - rect.Top) / dpi.DpiScaleY,
                    Math.Max(0, rect.Right - monitor.Work.Right) / dpi.DpiScaleX,
                    Math.Max(0, rect.Bottom - monitor.Work.Bottom) / dpi.DpiScaleY);
            }
        }
        window.SetValue(MaximizedInsetsPropertyKey, inset);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    private static void Minimize(object sender, ExecutedRoutedEventArgs e)
    { SystemCommands.MinimizeWindow((Window)sender); e.Handled = true; }
    private static void Maximize(object sender, ExecutedRoutedEventArgs e)
    { SystemCommands.MaximizeWindow((Window)sender); e.Handled = true; }
    private static void Restore(object sender, ExecutedRoutedEventArgs e)
    { SystemCommands.RestoreWindow((Window)sender); e.Handled = true; }
    private static void Close(object sender, ExecutedRoutedEventArgs e)
    { SystemCommands.CloseWindow((Window)sender); e.Handled = true; }
}
