using System.Windows;
using System.Windows.Controls;

namespace DiagnosticFlashTool.App.Views.Controls;

public sealed class HeaderedCard : HeaderedContentControl
{
    public static readonly DependencyProperty ActionsProperty = DependencyProperty.Register(
        nameof(Actions),
        typeof(object),
        typeof(HeaderedCard),
        new PropertyMetadata(null));

    public static readonly DependencyProperty HeaderHeightProperty = DependencyProperty.Register(
        nameof(HeaderHeight),
        typeof(GridLength),
        typeof(HeaderedCard),
        new PropertyMetadata(new GridLength(44)));

    public static readonly DependencyProperty HeaderPaddingProperty = DependencyProperty.Register(
        nameof(HeaderPadding),
        typeof(Thickness),
        typeof(HeaderedCard),
        new PropertyMetadata(new Thickness(16, 0, 16, 0)));

    public static readonly DependencyProperty BodyPaddingProperty = DependencyProperty.Register(
        nameof(BodyPadding),
        typeof(Thickness),
        typeof(HeaderedCard),
        new PropertyMetadata(new Thickness(16, 0, 16, 16)));

    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public GridLength HeaderHeight
    {
        get => (GridLength)GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public Thickness HeaderPadding
    {
        get => (Thickness)GetValue(HeaderPaddingProperty);
        set => SetValue(HeaderPaddingProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing around the card body. Use this property instead
    /// of the inherited Padding property so the header and body remain independent.
    /// </summary>
    public Thickness BodyPadding
    {
        get => (Thickness)GetValue(BodyPaddingProperty);
        set => SetValue(BodyPaddingProperty, value);
    }
}
