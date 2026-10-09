using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace AcerCareLite.App.Controls;

/// <summary>One settings row. Put the control (switch, choice list...) inside the element.</summary>
[ContentProperty(nameof(Control))]
public partial class SettingRow : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty =
        DependencyProperty.Register(nameof(Description), typeof(string), typeof(SettingRow), new PropertyMetadata(""));
    public static readonly DependencyProperty ControlProperty =
        DependencyProperty.Register(nameof(Control), typeof(object), typeof(SettingRow), new PropertyMetadata(null));
    public static readonly DependencyProperty ShowDividerProperty =
        DependencyProperty.Register(nameof(ShowDivider), typeof(bool), typeof(SettingRow), new PropertyMetadata(true, OnShowDividerChanged));

    public SettingRow() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
    public object? Control { get => GetValue(ControlProperty); set => SetValue(ControlProperty, value); }

    /// <summary>The thin line above the row; the first row of a card turns it off.</summary>
    public bool ShowDivider { get => (bool)GetValue(ShowDividerProperty); set => SetValue(ShowDividerProperty, value); }

    private static void OnShowDividerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingRow row && row.Content is Border border)
            border.BorderThickness = (bool)e.NewValue ? new Thickness(0, 1, 0, 0) : new Thickness(0);
    }
}
