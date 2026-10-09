using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace AcerCareLite.App.Controls;

public partial class SectionHeader : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty AsideProperty =
        DependencyProperty.Register(nameof(Aside), typeof(string), typeof(SectionHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(Geometry), typeof(SectionHeader), new PropertyMetadata(null));

    public SectionHeader() => InitializeComponent();

    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public string Aside { get => (string)GetValue(AsideProperty); set => SetValue(AsideProperty, value); }
    public Geometry? Icon { get => (Geometry?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
}
