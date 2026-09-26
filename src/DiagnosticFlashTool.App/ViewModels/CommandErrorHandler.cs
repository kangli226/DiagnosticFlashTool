using System.Diagnostics;

namespace DiagnosticFlashTool.App.ViewModels;

/// <summary>
/// Sink used by <see cref="RelayCommand"/> and <see cref="AsyncRelayCommand"/> to
/// report exceptions raised inside command handlers.
/// </summary>
/// <remarks>
/// Without a sink, an exception thrown by a command handler escapes the dispatcher
/// and terminates the process. The owning view model assigns <see cref="Current"/>
/// so failures are surfaced through the log and status bar instead.
/// </remarks>
internal static class CommandErrorHandler
{
    /// <summary>
    /// Handler assigned by the owning view model. When null, exceptions are only
    /// written to the debug output.
    /// </summary>
    public static Action<Exception>? Current { get; set; }

    public static void Report(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var handler = Current;
        if (handler is null)
        {
            Debug.WriteLine($"Unhandled command exception: {exception}");
            return;
        }

        try
        {
            handler(exception);
        }
        catch (Exception handlerException)
        {
            // Reporting must never itself become a crash source.
            Debug.WriteLine($"Command error handler failed: {handlerException}");
        }
    }
}
