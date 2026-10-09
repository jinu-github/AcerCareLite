using System.Windows;
using System.Windows.Controls;

namespace AcerCareLite.App.Controls;

public partial class PropertyRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(PropertyRow), new PropertyMetadata(""));
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(string), typeof(PropertyRow), new PropertyMetadata(""));
    public static readonly DependencyProperty ShowDividerProperty =
        DependencyProperty.Register(nameof(ShowDivider), typeof(bool), typeof(PropertyRow), new PropertyMetadata(true, OnShowDividerChanged));

    public PropertyRow() => InitializeComponent();

    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
    public string Value { get => (string)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public bool ShowDivider { get => (bool)GetValue(ShowDividerProperty); set => SetValue(ShowDividerProperty, value); }

    private static void OnShowDividerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PropertyRow row && row.Content is Border border)
            border.BorderThickness = (bool)e.NewValue ? new Thickness(0, 1, 0, 0) : new Thickness(0);
    }
}
