using System.Windows;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;

namespace TokenChecker.Controls;

// 基底クラスは XAML 側で定義済み（System.Windows.Controls.UserControl）
public partial class UsageBar
{
    private static readonly SolidColorBrush FrozenGreen  = Frozen(0x4C, 0xAF, 0x50);
    private static readonly SolidColorBrush FrozenYellow = Frozen(0xFF, 0xC1, 0x07);
    private static readonly SolidColorBrush FrozenRed    = Frozen(0xF4, 0x43, 0x36);

    private static SolidColorBrush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(MediaColor.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(UsageBar),
            new PropertyMetadata(0.0, (d, _) => ((UsageBar)d).UpdateBar()));

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public UsageBar()
    {
        InitializeComponent();
        SizeChanged += (_, _) => UpdateBar();
        Loaded      += (_, _) => UpdateBar();
    }

    private void UpdateBar()
    {
        var clamped = Math.Clamp(Value, 0, 1);
        Fill.Width = Root.ActualWidth * clamped;
        Fill.Fill  = clamped < 0.75 ? FrozenGreen
                   : clamped < 0.90 ? FrozenYellow
                   : FrozenRed;
    }
}
