using System.Globalization;
using System.Windows.Data;
using DiagnosticFlashTool.App.Utilities;

namespace DiagnosticFlashTool.App.Converters;

public sealed class FileNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => FilePathDisplayFormatter.GetFileName(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => System.Windows.Data.Binding.DoNothing;
}
