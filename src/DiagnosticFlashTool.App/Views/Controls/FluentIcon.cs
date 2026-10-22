using System.Windows;
using System.Windows.Controls;
using FontFamily = System.Windows.Media.FontFamily;
using HorizontalAlignment = System.Windows.HorizontalAlignment;

namespace DiagnosticFlashTool.App.Views.Controls;

/// <summary>
/// 应用内可复用的 Fluent 系统图标。
/// </summary>
public sealed class FluentIcon : TextBlock
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(
        nameof(Kind),
        typeof(FluentIconKind),
        typeof(FluentIcon),
        new FrameworkPropertyMetadata(FluentIconKind.ArrowRight, OnKindChanged));

    static FluentIcon()
    {
        FontSizeProperty.OverrideMetadata(typeof(FluentIcon), new FrameworkPropertyMetadata(16d));
        TextAlignmentProperty.OverrideMetadata(typeof(FluentIcon), new FrameworkPropertyMetadata(TextAlignment.Center));
        HorizontalAlignmentProperty.OverrideMetadata(typeof(FluentIcon), new FrameworkPropertyMetadata(HorizontalAlignment.Center));
        VerticalAlignmentProperty.OverrideMetadata(typeof(FluentIcon), new FrameworkPropertyMetadata(VerticalAlignment.Center));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(FluentIcon), new FrameworkPropertyMetadata(true));
    }

    public FluentIcon()
    {
        // 图标使用符号字体，不继承页面正文字体；字号和对齐仍可由共享样式设置。
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        FontWeight = FontWeights.Normal;
        UpdateGlyph();
    }

    public FluentIconKind Kind
    {
        get => (FluentIconKind)GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    private static void OnKindChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        ((FluentIcon)dependencyObject).UpdateGlyph();
    }

    private void UpdateGlyph()
    {
        Text = Kind switch
        {
            FluentIconKind.ArrowRight => "\uE72A",
            FluentIconKind.CheckCircleOutline => "\uE930",
            FluentIconKind.ChevronDown => "\uE70D",
            FluentIconKind.ChevronRight => "\uE76C",
            FluentIconKind.Close => "\uE8BB",
            FluentIconKind.CloseCircleOutline => "\uE711",
            FluentIconKind.CloudDownloadOutline => "\uE896",
            FluentIconKind.CogOutline => "\uE713",
            FluentIconKind.Console => "\uE756",
            FluentIconKind.ContentSaveOutline => "\uE74E",
            FluentIconKind.DeleteOutline => "\uE74D",
            FluentIconKind.DeleteSweepOutline => "\uE74D",
            FluentIconKind.ExportVariant => "\uE72D",
            FluentIconKind.FileTreeOutline => "\uE8FD",
            FluentIconKind.FilterOutline => "\uE71C",
            FluentIconKind.FlashOutline => "\uE945",
            FluentIconKind.FolderCogOutline => "\uE912",
            FluentIconKind.FolderOpenOutline => "\uE838",
            FluentIconKind.FolderPlusOutline => "\uE8F4",
            FluentIconKind.FunctionVariant => "\uE8EF",
            FluentIconKind.HandPointingDown => "\uE74B",
            FluentIconKind.HandPointingUp => "\uE74A",
            FluentIconKind.History => "\uE81C",
            FluentIconKind.Pause => "\uE769",
            FluentIconKind.Plus => "\uE710",
            FluentIconKind.PowerPlugOutline => "\uE839",
            FluentIconKind.Refresh => "\uE72C",
            FluentIconKind.SendOutline => "\uE724",
            FluentIconKind.TextBoxOutline => "\uE8A5",
            FluentIconKind.WarningOutline => "\uE7BA",
            FluentIconKind.WindowMaximize => "\uE922",
            FluentIconKind.WindowMinimize => "\uE921",
            _ => "\uE946"
        };
    }
}

public enum FluentIconKind
{
    ArrowRight,
    CheckCircleOutline,
    ChevronDown,
    ChevronRight,
    Close,
    CloseCircleOutline,
    CloudDownloadOutline,
    CogOutline,
    Console,
    ContentSaveOutline,
    DeleteOutline,
    DeleteSweepOutline,
    ExportVariant,
    FileTreeOutline,
    FilterOutline,
    FlashOutline,
    FolderCogOutline,
    FolderOpenOutline,
    FolderPlusOutline,
    FunctionVariant,
    HandPointingDown,
    HandPointingUp,
    History,
    Pause,
    Plus,
    PowerPlugOutline,
    Refresh,
    SendOutline,
    TextBoxOutline,
    WarningOutline,
    WindowMaximize,
    WindowMinimize
}
