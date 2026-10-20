using System.Windows;

namespace DiagnosticFlashTool.App.Views.Controls;

/// <summary>
/// 为官方 Fluent 按钮模板提供可按业务状态切换的局部画刷资源。
/// </summary>
public static class FluentButtonAssist
{
    public static readonly DependencyProperty StateResourcesProperty = DependencyProperty.RegisterAttached(
        "StateResources",
        typeof(ResourceDictionary),
        typeof(FluentButtonAssist),
        new PropertyMetadata(null, OnStateResourcesChanged));

    public static ResourceDictionary? GetStateResources(DependencyObject element) =>
        (ResourceDictionary?)element.GetValue(StateResourcesProperty);

    public static void SetStateResources(DependencyObject element, ResourceDictionary? value) =>
        element.SetValue(StateResourcesProperty, value);

    private static void OnStateResourcesChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        if (dependencyObject is not FrameworkElement element)
        {
            return;
        }

        if (eventArgs.OldValue is ResourceDictionary previous)
        {
            element.Resources.MergedDictionaries.Remove(previous);
        }

        if (eventArgs.NewValue is ResourceDictionary current)
        {
            element.Resources.MergedDictionaries.Add(current);
        }
    }
}
