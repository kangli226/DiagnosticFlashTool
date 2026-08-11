using System.Windows;
using System.Windows.Controls;

namespace DiagnosticFlashTool.App.Views.Controls;

public static class PasswordBoxBinding
{
    public static readonly DependencyProperty BoundPasswordProperty = DependencyProperty.RegisterAttached(
        "BoundPassword",
        typeof(string),
        typeof(PasswordBoxBinding),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundPasswordChanged));

    private static readonly DependencyProperty IsUpdatingProperty = DependencyProperty.RegisterAttached(
        "IsUpdating",
        typeof(bool),
        typeof(PasswordBoxBinding),
        new PropertyMetadata(false));

    public static string GetBoundPassword(DependencyObject element)
    {
        return element.GetValue(BoundPasswordProperty) as string ?? string.Empty;
    }

    public static void SetBoundPassword(DependencyObject element, string value)
    {
        element.SetValue(BoundPasswordProperty, value);
    }

    private static bool GetIsUpdating(DependencyObject element)
    {
        return (bool)element.GetValue(IsUpdatingProperty);
    }

    private static void SetIsUpdating(DependencyObject element, bool value)
    {
        element.SetValue(IsUpdatingProperty, value);
    }

    private static void OnBoundPasswordChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not PasswordBox passwordBox || GetIsUpdating(passwordBox))
        {
            return;
        }

        passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
        passwordBox.Password = e.NewValue as string ?? string.Empty;
        passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
    }

    private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is not PasswordBox passwordBox)
        {
            return;
        }

        SetIsUpdating(passwordBox, true);
        SetBoundPassword(passwordBox, passwordBox.Password);
        SetIsUpdating(passwordBox, false);
    }
}
