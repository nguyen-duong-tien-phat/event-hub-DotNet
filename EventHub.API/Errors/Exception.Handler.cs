using Microsoft.AspNetCore.Diagnostics;

namespace EventHub.Errors;

/// <summary>
/// Last line of defence: whatever a controller did not turn into a status code ends here. Clients get the
/// same { message } body the controllers use, and the log gets the exception with the request it came from.
/// </summary>
public class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : IExceptionHandler {
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken) {
        // The caller hung up (closed the tab, timed out): there is nobody to answer and nothing to fix.
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested) {
            logger.LogInformation("{Method} {Path} was cancelled by the client", context.Request.Method, context.Request.Path);
            return true;
        }

        logger.LogError(exception, "Unhandled exception on {Method} {Path} (request {TraceId})",
            context.Request.Method, context.Request.Path, context.TraceIdentifier);

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        // The traceId lets support find this error in the log. The exception text never leaves the server.
        await context.Response.WriteAsJsonAsync(
            new { message = "Something went wrong. Please try again.", traceId = context.TraceIdentifier },
            cancellationToken);
        return true;
    }
}
