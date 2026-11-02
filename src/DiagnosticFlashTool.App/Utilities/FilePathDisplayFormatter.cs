using System.IO;

namespace DiagnosticFlashTool.App.Utilities;

public static class FilePathDisplayFormatter
{
    public static string GetFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        try
        {
            var fileName = Path.GetFileName(path);
            return string.IsNullOrWhiteSpace(fileName) ? path : fileName;
        }
        catch
        {
            return path;
        }
    }
}
