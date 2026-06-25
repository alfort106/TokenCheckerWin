using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using TokenChecker.Utilities;
using TokenChecker.ViewModels;
using MediaColor = System.Windows.Media.Color;

namespace TokenChecker.Views;

public partial class TaskbarWidget : Window
{
    private enum DisplayMode { Wide, Compact, Vertical, AboveTaskbar }

    private const double WideWidth = 118;
    private const double CompactWidth = 70;
    private const double VerticalWidth = 44;
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOSIZE     = 0x0001;
    private const uint SWP_NOMOVE     = 0x0002;
    private const uint SWP_NOREDRAW   = 0x0008;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const int  GWL_EXSTYLE     = -20;
    private const long WS_EX_APPWINDOW = 0x00040000L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const int  SW_HIDE        = 0;
    private const int  SW_SHOWNA      = 8;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private readonly UsageViewModel _vm;
    private DispatcherTimer?        _topmostTimer;
    private int                     _screenIndex;
    private int                     _positionTick;

    public event Action? PopupToggleRequested;

    public int CurrentScreenIndex => _screenIndex;

    public TaskbarWidget(UsageViewModel vm, int initialScreenIndex = 0)
    {
        _vm = vm;
        _screenIndex = initialScreenIndex;
        InitializeComponent();
        SourceInitialized += (_, _) => HideFromAltTab(new WindowInteropHelper(this).Handle);
        Loaded += OnLoaded;
        vm.SnapshotChanged += () => Dispatcher.Invoke(UpdateLabels);
        UpdateLabels();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // ウィジェットはタスクバーに溶け込ませるため、DWM 効果は適用しない。
        // AllowsTransparency=True のレイヤードウィンドウにアクリルを掛けると
        // クリック時の再描画でコンテンツが消える競合が起きるため。
        SnapToTaskbar(_screenIndex, _vm.WidgetPlacement);

        // 全仮想デスクトップに固定（SetPropW が有効な環境ではポーリング不要になる）
        var initialHwnd = new WindowInteropHelper(this).Handle;
        HideFromAltTab(initialHwnd);
        VirtualDesktopHelper.PinToAllDesktops(initialHwnd);

        // タスクバーが前面に来てウィジェットを覆うため、定期的に最前面を再設定する。
        // 仮想デスクトップの切り替えも検知し、現在のデスクトップへ追従する。
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _topmostTimer.Tick += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (!VirtualDesktopHelper.IsOnCurrentDesktop(hwnd))
            {
                // 仮想デスクトップが切り替わった → 現在のデスクトップへ移動して再表示
                VirtualDesktopHelper.MoveToCurrentDesktop(hwnd);
            }
            if (IsFullscreenAppOnSelectedScreen(hwnd))
            {
                ShowWindow(hwnd, SW_HIDE);
                return;
            }

            ShowWindow(hwnd, SW_SHOWNA);
            ReassertTopmost();
            if (++_positionTick % 5 == 0)
                PositionOnSelectedTaskbar(_vm.WidgetPlacement);
        };
        _topmostTimer.Start();
        ReassertTopmost();
    }

    private void ReassertTopmost()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOREDRAW);
    }

    private static void HideFromAltTab(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;

        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle &= ~WS_EX_APPWINDOW;
        exStyle |= WS_EX_TOOLWINDOW;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    private static IntPtr GetWindowLongPtr(IntPtr hwnd, int index)
        => IntPtr.Size == 8
            ? GetWindowLongPtr64(hwnd, index)
            : new IntPtr(GetWindowLong32(hwnd, index));

    private static IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value)
        => IntPtr.Size == 8
            ? SetWindowLongPtr64(hwnd, index, value)
            : new IntPtr(SetWindowLong32(hwnd, index, value.ToInt32()));

    // ── タスクバー隣接配置 ────────────────────────────────────────────────

    private bool IsFullscreenAppOnSelectedScreen(IntPtr selfHwnd)
    {
        var foreground = GetForegroundWindow();
        if (foreground == IntPtr.Zero ||
            foreground == selfHwnd ||
            foreground == GetShellWindow() ||
            !IsWindowVisible(foreground))
            return false;

        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return false;

        var target = _screenIndex >= 0 && _screenIndex < screens.Length
            ? screens[_screenIndex]
            : screens[0];
        var foregroundScreen = System.Windows.Forms.Screen.FromHandle(foreground);
        if (!string.Equals(foregroundScreen.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase))
            return false;

        return GetWindowRect(foreground, out var rect) && CoversScreen(rect, target.Bounds);
    }

    private static bool CoversScreen(RECT rect, System.Drawing.Rectangle screenBounds)
    {
        const int tolerance = 2;
        return rect.Left <= screenBounds.Left + tolerance &&
               rect.Top <= screenBounds.Top + tolerance &&
               rect.Right >= screenBounds.Right - tolerance &&
               rect.Bottom >= screenBounds.Bottom - tolerance;
    }

    public void SnapToTaskbar(int screenIndex, WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        _screenIndex = screenIndex >= 0 && screenIndex < screens.Length ? screenIndex : 0;
        PositionOnSelectedTaskbar(placement);
    }

    private sealed record Layout(
        WidgetPlacement Placement,
        TaskbarPosition.Info Taskbar,
        double LeftSlot,
        double AvailableWidth);

    private void PositionOnSelectedTaskbar(WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;
        if (screens.Length == 0) return;

        var selected = CreateLayout(_screenIndex, placement);
        if (selected != null)
        {
            ApplyLayout(selected);
            return;
        }

        PositionAtScreenEdge(_screenIndex, placement);
    }

    private static Layout? CreateLayout(int screenIndex, WidgetPlacement placement)
    {
        var tb = TaskbarPosition.Get(screenIndex);
        if (tb == null) return null;

        var leftSlot = (tb.WidgetsRight ?? tb.TaskbarLeft) + 4;
        var available = placement == WidgetPlacement.Left
            ? (tb.ContentLeft ?? tb.NotifyLeft) - leftSlot - 4
            : tb.NotifyLeft - (tb.ContentRight ?? (tb.WidgetsRight ?? tb.TaskbarLeft)) - 6;
        return new Layout(placement, tb, leftSlot, available);
    }

    private void ApplyLayout(Layout layout)
    {
        Height = layout.Taskbar.TaskbarHeight;
        var mode = ApplyDisplayMode(layout.AvailableWidth);
        Left = layout.Placement == WidgetPlacement.Left
            ? layout.LeftSlot
            : layout.Taskbar.NotifyLeft - Width - 2;
        Top = mode == DisplayMode.AboveTaskbar
            ? layout.Taskbar.TaskbarTop - Height - 4
            : layout.Taskbar.TaskbarTop;
    }

    private void PositionAtScreenEdge(int screenIndex, WidgetPlacement placement)
    {
        var screens = System.Windows.Forms.Screen.AllScreens;

        // Taskbar inspection failed: keep the widget visible at the selected screen edge.
        var screen = screenIndex < screens.Length ? screens[screenIndex] : screens[0];
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        var dpi = g.DpiX / 96.0;

        // タスクバー相当の高さを推定（プライマリから取得できる場合は利用）
        double tbHeight = TaskbarPosition.Get()?.TaskbarHeight ?? 48 / dpi;
        Height = tbHeight;
        ApplyDisplayMode(WideWidth);
        var fallbackRightSlot = screen.Bounds.Right / dpi - Width - 4;
        Left   = placement == WidgetPlacement.Left
            ? fallbackRightSlot - ActualWidth - 6
            : fallbackRightSlot;
        Top    = screen.Bounds.Bottom / dpi - tbHeight;
    }

    // ── ラベル更新 ────────────────────────────────────────────────────────

    private void UpdateLabels()
    {
        var snap       = _vm.Snapshot;
        var claudeUtil = snap.ClaudeUsage?.FiveHour?.Utilization ?? snap.ClaudeUsage?.Weekly?.Utilization;
        var codexUtil  = snap.CodexUsage?.FiveHour?.Utilization  ?? snap.CodexUsage?.Weekly?.Utilization;

        ClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}%" : "--%";
        ClaudeLabel.Foreground = UtilBrush(claudeUtil);
        CodexLabel.Text        = codexUtil.HasValue  ? $"{(int)(codexUtil.Value  * 100)}%" : "--%";
        CodexLabel.Foreground  = UtilBrush(codexUtil);
        CompactClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}" : "--";
        CompactClaudeLabel.Foreground = UtilBrush(claudeUtil);
        CompactCodexLabel.Text        = codexUtil.HasValue ? $"{(int)(codexUtil.Value * 100)}" : "--";
        CompactCodexLabel.Foreground  = UtilBrush(codexUtil);
        VerticalClaudeLabel.Text       = claudeUtil.HasValue ? $"{(int)(claudeUtil.Value * 100)}" : "--";
        VerticalClaudeLabel.Foreground = UtilBrush(claudeUtil);
        VerticalCodexLabel.Text        = codexUtil.HasValue ? $"{(int)(codexUtil.Value * 100)}" : "--";
        VerticalCodexLabel.Foreground  = UtilBrush(codexUtil);
    }

    private DisplayMode ApplyDisplayMode(double availableWidth)
    {
        var mode = availableWidth >= WideWidth ? DisplayMode.Wide
            : availableWidth >= CompactWidth ? DisplayMode.Compact
            : availableWidth >= VerticalWidth ? DisplayMode.Vertical
            : DisplayMode.AboveTaskbar;

        Width = mode switch
        {
            DisplayMode.Wide => WideWidth,
            DisplayMode.Compact => CompactWidth,
            DisplayMode.Vertical => VerticalWidth,
            _ => WideWidth,
        };
        WideContent.Visibility = mode is DisplayMode.Wide or DisplayMode.AboveTaskbar
            ? Visibility.Visible
            : Visibility.Collapsed;
        CompactContent.Visibility = mode == DisplayMode.Compact ? Visibility.Visible : Visibility.Collapsed;
        VerticalContent.Visibility = mode == DisplayMode.Vertical ? Visibility.Visible : Visibility.Collapsed;
        return mode;
    }

    private static readonly System.Windows.Media.SolidColorBrush BrushGray   = Frozen(0x90, 0x90, 0x90);
    private static readonly System.Windows.Media.SolidColorBrush BrushGreen  = Frozen(0x4C, 0xAF, 0x50);
    private static readonly System.Windows.Media.SolidColorBrush BrushYellow = Frozen(0xFF, 0xC1, 0x07);
    private static readonly System.Windows.Media.SolidColorBrush BrushRed    = Frozen(0xF4, 0x43, 0x36);

    private static System.Windows.Media.SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var b2 = new System.Windows.Media.SolidColorBrush(MediaColor.FromRgb(r, g, b));
        b2.Freeze();
        return b2;
    }

    private static System.Windows.Media.SolidColorBrush UtilBrush(double? v)
        => v == null ? BrushGray
         : v < 0.75  ? BrushGreen
         : v < 0.90  ? BrushYellow
         : BrushRed;

    // ── クリックで詳細ポップアップを開閉 ─────────────────────────────────

    private void Root_Click(object sender, MouseButtonEventArgs e)
        => PopupToggleRequested?.Invoke();
}
