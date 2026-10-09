using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AcerCareLite.App.Controls;

/// <summary>A live-reading card: icon and label, a large value with a small unit, a description line and a progress bar.</summary>
public partial class StatCard : UserControl
{
    public static readonly DependencyProperty TitleProperty = Register(nameof(Title), typeof(string), "");
    public static readonly DependencyProperty ValueProperty = Register(nameof(Value), typeof(string), "");
    public static readonly DependencyProperty UnitProperty = Register(nameof(Unit), typeof(string), "");
    public static readonly DependencyProperty DescriptionProperty = Register(nameof(Description), typeof(string), "");
    public static readonly DependencyProperty ProgressProperty = Register(nameof(Progress), typeof(double), 0.0);
    public static readonly DependencyProperty IconProperty = Register(nameof(Icon), typeof(Geometry), null);
    public static readonly DependencyProperty IconBrushProperty = Register(nameof(IconBrush), typeof(Brush), null);
    public static readonly DependencyProperty ProgressBrushProperty = Register(nameof(ProgressBrush), typeof(Brush), null);

    public StatCard()
    {
        InitializeComponent();
        // Defaults follow the theme; a value set in XAML afterwards replaces these references.
        SetResourceReference(IconBrushProperty, "AccentBrush");
        SetResourceReference(ProgressBrushProperty, "AccentBrush");
    }

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public string Unit { get => (string)GetValue(UnitProperty); set => SetValue(UnitProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush? IconBrush { get => (Brush?)GetValue(IconBrushProperty); set => SetValue(IconBrushProperty, value); }
    public Brush? ProgressBrush { get => (Brush?)GetValue(ProgressBrushProperty); set => SetValue(ProgressBrushProperty, value); }

    private static DependencyProperty Register(string name, Type type, object? defaultValue) =>
        DependencyProperty.Register(name, type, typeof(StatCard), new PropertyMetadata(defaultValue));
}
