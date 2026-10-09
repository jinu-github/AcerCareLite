using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AcerCareLite.App.Controls;

/// <summary>Small, shared motion helpers. Everything is short and is skipped when Windows animation effects are off.</summary>
public static class Motion
{
    /// <summary>A quick fade with a few pixels of upward travel, used when a page appears.</summary>
    public static void PageEnter(FrameworkElement element)
    {
        if (!SystemParameters.ClientAreaAnimation) return;

        var move = new TranslateTransform(0, 8);
        element.RenderTransform = move;
        element.Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = ease };
        fade.Completed += (_, _) =>
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            element.RenderTransform = Transform.Identity;
        };
        element.BeginAnimation(UIElement.OpacityProperty, fade);
        move.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
    }
}
