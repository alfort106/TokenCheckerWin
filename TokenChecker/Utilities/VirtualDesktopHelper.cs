using System.Runtime.InteropServices;

namespace TokenChecker.Utilities;

/// <summary>
/// Windows 仮想デスクトップ API のラッパー。
/// ウィンドウを全デスクトップに固定し、切り替え時の追従を担う。
/// </summary>
public static class VirtualDesktopHelper
{
    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("a5cd92ff-29be-454c-8d04-d82879fb3f1b")]
    private interface IVirtualDesktopManager
    {
        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsWindowOnCurrentVirtualDesktop(IntPtr topLevelWindow);
        Guid GetWindowDesktopId(IntPtr topLevelWindow);
        void MoveWindowToDesktop(IntPtr topLevelWindow, ref Guid desktopId);
    }

    [ComImport, Guid("aa509086-5ca9-4c25-8f95-589d3c07b48a")]
    private class CVirtualDesktopManager { }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SetPropW(IntPtr hWnd, string lpString, IntPtr hData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string? windowName,
        uint style, int x, int y, int w, int h,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr hwnd);

    private const uint WS_POPUP = 0x80000000;

    private static readonly IVirtualDesktopManager? _manager;

    static VirtualDesktopHelper()
    {
        try { _manager = (IVirtualDesktopManager)new CVirtualDesktopManager(); }
        catch { }
    }

    /// <summary>
    /// ウィンドウを全仮想デスクトップに固定する。
    /// Windows 10/11 で有効な SetPropW アプローチを使用。
    /// 非対応環境向けに IsOnCurrentDesktop + MoveToCurrentDesktop がフォールバックとして機能する。
    /// </summary>
    public static void PinToAllDesktops(IntPtr hwnd)
    {
        try { SetPropW(hwnd, "VirtualDesktopPinned", new IntPtr(1)); }
        catch { }
    }

    /// <summary>指定ウィンドウが現在アクティブな仮想デスクトップにあるか。</summary>
    public static bool IsOnCurrentDesktop(IntPtr hwnd)
    {
        if (_manager == null || hwnd == IntPtr.Zero) return true;
        try { return _manager.IsWindowOnCurrentVirtualDesktop(hwnd); }
        catch { return true; }
    }

    /// <summary>
    /// 指定ウィンドウを現在の仮想デスクトップへ移動する。
    /// フォアグラウンドウィンドウで現在のデスクトップIDを特定し、
    /// 取得できない場合（空の新規デスクトップ等）は一時ウィンドウで補完する。
    /// </summary>
    public static void MoveToCurrentDesktop(IntPtr hwnd)
    {
        if (_manager == null || hwnd == IntPtr.Zero) return;
        try
        {
            var desktopId = GetCurrentDesktopId();
            if (desktopId == Guid.Empty) return;
            _manager.MoveWindowToDesktop(hwnd, ref desktopId);
        }
        catch { }
    }

    private static Guid GetCurrentDesktopId()
    {
        if (_manager == null) return Guid.Empty;

        // 1. フォアグラウンドウィンドウから現在のデスクトップIDを取得
        var fg = GetForegroundWindow();
        if (fg != IntPtr.Zero)
        {
            try
            {
                var id = _manager.GetWindowDesktopId(fg);
                if (id != Guid.Empty) return id;
            }
            catch { }
        }

        // 2. 空の新規デスクトップの場合: 一時ポップアップウィンドウを作成してIDを取得
        //    新規作成されたウィンドウは常に現在のアクティブデスクトップに割り当てられる
        var probe = CreateWindowEx(0, "STATIC", null, WS_POPUP,
            -1, -1, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (probe != IntPtr.Zero)
        {
            try { return _manager.GetWindowDesktopId(probe); }
            catch { }
            finally { DestroyWindow(probe); }
        }

        return Guid.Empty;
    }
}
