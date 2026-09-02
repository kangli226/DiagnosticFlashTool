using System.Windows;
using WpfFrameworkElement = System.Windows.FrameworkElement;
using WpfPanel = System.Windows.Controls.Panel;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;
using WpfUiElement = System.Windows.UIElement;
using WpfVisibility = System.Windows.Visibility;

namespace DiagnosticFlashTool.App.Views.Controls;

/// <summary>
/// Lays out a card title followed by its data rows using the shared card rhythm.
/// The first visible child is treated as the title; the remaining visible
/// children are data rows. Card padding is supplied by the containing Border.
/// </summary>
public sealed class CardContentPanel : WpfPanel
{
    public static readonly DependencyProperty HeaderSpacingProperty = DependencyProperty.Register(
        nameof(HeaderSpacing),
        typeof(double),
        typeof(CardContentPanel),
        new FrameworkPropertyMetadata(10d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowSpacingProperty = DependencyProperty.Register(
        nameof(RowSpacing),
        typeof(double),
        typeof(CardContentPanel),
        new FrameworkPropertyMetadata(12d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty LastRowBottomSpacingProperty = DependencyProperty.Register(
        nameof(LastRowBottomSpacing),
        typeof(double),
        typeof(CardContentPanel),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double HeaderSpacing
    {
        get => (double)GetValue(HeaderSpacingProperty);
        set => SetValue(HeaderSpacingProperty, value);
    }

    public double RowSpacing
    {
        get => (double)GetValue(RowSpacingProperty);
        set => SetValue(RowSpacingProperty, value);
    }

    public double LastRowBottomSpacing
    {
        get => (double)GetValue(LastRowBottomSpacingProperty);
        set => SetValue(LastRowBottomSpacingProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var visibleChildren = GetVisibleChildren();
        var childConstraint = new WpfSize(availableSize.Width, double.PositiveInfinity);
        var desiredWidth = 0d;
        var desiredHeight = 0d;

        for (var index = 0; index < visibleChildren.Count; index++)
        {
            var child = visibleChildren[index];
            child.Measure(childConstraint);

            desiredWidth = Math.Max(desiredWidth, child.DesiredSize.Width);
            desiredHeight += child.DesiredSize.Height;

            if (index > 0)
            {
                desiredHeight += GetAdditionalSpacing(
                    visibleChildren[index - 1],
                    child,
                    index == 1 ? HeaderSpacing : RowSpacing);
            }
        }

        if (visibleChildren.Count > 0)
        {
            desiredHeight += GetAdditionalBottomSpacing(
                visibleChildren[^1],
                LastRowBottomSpacing);
        }

        return new WpfSize(
            double.IsPositiveInfinity(availableSize.Width)
                ? desiredWidth
                : Math.Min(desiredWidth, availableSize.Width),
            desiredHeight);
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        var visibleChildren = GetVisibleChildren();
        var offset = 0d;

        for (var index = 0; index < visibleChildren.Count; index++)
        {
            var child = visibleChildren[index];

            if (index > 0)
            {
                offset += GetAdditionalSpacing(
                    visibleChildren[index - 1],
                    child,
                    index == 1 ? HeaderSpacing : RowSpacing);
            }

            child.Arrange(new WpfRect(0, offset, finalSize.Width, child.DesiredSize.Height));
            offset += child.DesiredSize.Height;
        }

        return finalSize;
    }

    private List<WpfUiElement> GetVisibleChildren()
    {
        var visibleChildren = new List<WpfUiElement>(InternalChildren.Count);

        foreach (WpfUiElement child in InternalChildren)
        {
            if (child.Visibility != WpfVisibility.Collapsed)
            {
                visibleChildren.Add(child);
            }
        }

        return visibleChildren;
    }

    private static double Positive(double value) => double.IsFinite(value) ? Math.Max(0d, value) : 0d;

    private static double GetAdditionalSpacing(WpfUiElement previous, WpfUiElement next, double targetSpacing)
    {
        var previousBottomMargin = previous is WpfFrameworkElement previousElement
            ? previousElement.Margin.Bottom
            : 0d;
        var nextTopMargin = next is WpfFrameworkElement nextElement
            ? nextElement.Margin.Top
            : 0d;

        // Margins are already included in DesiredSize. Subtract them from the
        // panel-owned gap so the distance between the visible child contents
        // remains the configured value when a control style contributes a
        // vertical margin. Keep an existing larger margin intact.
        return Math.Max(
            0d,
            Positive(targetSpacing) - Positive(previousBottomMargin) - Positive(nextTopMargin));
    }

    private static double GetAdditionalBottomSpacing(WpfUiElement lastChild, double targetSpacing)
    {
        var lastBottomMargin = lastChild is WpfFrameworkElement lastElement
            ? lastElement.Margin.Bottom
            : 0d;

        return Math.Max(0d, Positive(targetSpacing) - Positive(lastBottomMargin));
    }
}
