using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace QuranWidget;

internal static class Native
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hWndParent, IntPtr hWndChildAfter, string lpszClass, string? lpszWindow);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam, uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    public const uint SMTO_NORMAL = 0x0000;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    [DllImport("user32.dll")]
    public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

    [StructLayout(LayoutKind.Sequential)]
    public struct WindowCompositionAttributeData
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AccentPolicy
    {
        public int AccentState;
        public uint AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const int WS_CHILD = 0x40000000;
    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int ACCENT_DISABLED = 0;
    public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    public const int WCA_ACCENT_POLICY = 19;
}

internal static class WindowEffects
{
    public static void EnableAcrylic(Window w)
    {
        try
        {
            uint tint = ThemeService.AcrylicTint;
            if (tint == 0) { DisableAcrylic(w); return; }
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).EnsureHandle();
            var accent = new Native.AccentPolicy
            {
                AccentState = Native.ACCENT_ENABLE_ACRYLICBLURBEHIND,
                AccentFlags = 2,
                GradientColor = tint,
            };
            int size = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new Native.WindowCompositionAttributeData
                {
                    Attribute = Native.WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size,
                };
                Native.SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
            int round = 2; // DWMWCP_ROUND
            Native.DwmSetWindowAttribute(hwnd, 33, ref round, 4);
        }
        catch
        {
            // acrylic is a nice-to-have; the card looks good without it
        }
    }

    public static void DisableAcrylic(Window w)
    {
        try
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(w).EnsureHandle();
            var accent = new Native.AccentPolicy { AccentState = Native.ACCENT_DISABLED };
            int size = Marshal.SizeOf(accent);
            var ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new Native.WindowCompositionAttributeData
                {
                    Attribute = Native.WCA_ACCENT_POLICY,
                    Data = ptr,
                    SizeOfData = size,
                };
                Native.SetWindowCompositionAttribute(hwnd, ref data);
            }
            finally
            {
                Marshal.FreeHGlobal(ptr);
            }
        }
        catch
        {
        }
    }

    // "Pin to desktop" keeps the window top-level (so WPF transparency and input keep
    // working) but holds it at the very bottom of the desktop, behind every other
    // window, just above the wallpaper. MainWindow re-asserts the bottom position.
    public static bool PinToDesktop(IntPtr hwnd)
    {
        try
        {
            int exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, exStyle | Native.WS_EX_TOOLWINDOW);
            SendToBottom(hwnd);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void SendToBottom(IntPtr hwnd)
    {
        try
        {
            Native.SetWindowPos(hwnd, (IntPtr)1 /*HWND_BOTTOM*/, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }
        catch
        {
        }
    }

    public static void UnpinFromDesktop(IntPtr hwnd)
    {
        try
        {
            int exStyle = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, exStyle & ~Native.WS_EX_TOOLWINDOW);
            Native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_FRAMECHANGED | Native.SWP_SHOWWINDOW);
        }
        catch
        {
        }
    }
}
