using System.Windows;
using System.Windows.Media;

namespace DiagnosticFlashTool.App.Views.Controls;

/// <summary>
/// Clips a control to a rounded rectangle so content rendered by the
/// control's own template follows the same radius as its outer border.
/// </summary>
public static class CornerRadiusClipBehavior
{
    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached(
        "CornerRadius",
        typeof(CornerRadius),
        typeof(CornerRadiusClipBehavior),
        new FrameworkPropertyMetadata(new CornerRadius(0), OnCornerRadiusChanged));

    public static void SetCornerRadius(DependencyObject element, CornerRadius value) =>
        element.SetValue(CornerRadiusProperty, value);

    public static CornerRadius GetCornerRadius(DependencyObject element) =>
        (CornerRadius)element.GetValue(CornerRadiusProperty);

    private static void OnCornerRadiusChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        element.Loaded -= Element_Loaded;
        element.SizeChanged -= Element_SizeChanged;

        var radius = (CornerRadius)args.NewValue;
        if (radius.TopLeft <= 0 && radius.TopRight <= 0 && radius.BottomRight <= 0 && radius.BottomLeft <= 0)
        {
            element.ClearValue(UIElement.ClipProperty);
            return;
        }

        element.Loaded += Element_Loaded;
        element.SizeChanged += Element_SizeChanged;
        UpdateClip(element);
    }

    private static void Element_Loaded(object sender, RoutedEventArgs args)
    {
        if (sender is FrameworkElement element)
        {
            UpdateClip(element);
        }
    }

    private static void Element_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is FrameworkElement element)
        {
            UpdateClip(element);
        }
    }

    private static void UpdateClip(FrameworkElement element)
    {
        if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
        {
            return;
        }

        var radius = GetCornerRadius(element);
        var uniformRadius = Math.Max(
            Math.Max(radius.TopLeft, radius.TopRight),
            Math.Max(radius.BottomRight, radius.BottomLeft));
        var maxRadius = Math.Min(element.ActualWidth, element.ActualHeight) / 2;

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            Math.Min(uniformRadius, maxRadius),
            Math.Min(uniformRadius, maxRadius));
    }
}
