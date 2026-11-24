using System.Windows;
using ForgeDesk.Core.Models;

namespace ForgeDesk.Infrastructure;

/// <summary>Saves and restores the main window's bounds, keeping it on a visible screen.</summary>
internal static class WindowPlacementHelper
{
    public static void Restore(Window window, WindowPlacement? placement)
    {
        var work = SystemParameters.WorkArea;
        if (placement is null || !placement.IsUsable || !IsReachable(placement))
        {
            window.Width = Math.Max(window.MinWidth, Math.Round(work.Width * 0.85));
            window.Height = Math.Max(window.MinHeight, Math.Round(work.Height * 0.85));
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = placement.Left;
        window.Top = placement.Top;
        window.Width = Math.Max(window.MinWidth, placement.Width);
        window.Height = Math.Max(window.MinHeight, placement.Height);
        if (placement.Maximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    public static WindowPlacement Capture(Window window)
    {
        var bounds = window.WindowState == WindowState.Normal
            ? new Rect(window.Left, window.Top, window.ActualWidth, window.ActualHeight)
            : window.RestoreBounds;
        return new WindowPlacement
        {
            Left = bounds.Left,
            Top = bounds.Top,
            Width = bounds.Width,
            Height = bounds.Height,
            Maximized = window.WindowState == WindowState.Maximized,
        };
    }

    private static bool IsReachable(WindowPlacement p)
    {
        // At least a strip of the title bar must be on the virtual desktop.
        var desktop = new Rect(
            SystemParameters.VirtualScreenLeft,
            SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth,
            SystemParameters.VirtualScreenHeight);
        var titleStrip = new Rect(p.Left + 40, p.Top, Math.Max(1, p.Width - 80), 32);
        return desktop.IntersectsWith(titleStrip);
    }
}
