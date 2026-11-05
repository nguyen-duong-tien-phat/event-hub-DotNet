using System.Text.Json;
using EventHub.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;

namespace EventHub.Tests;

public class ExceptionHandlerTests {
    private readonly Mock<ILogger<GlobalExceptionHandler>> _logger = new();

    private static DefaultHttpContext NewContext(CancellationToken aborted = default) {
        var context = new DefaultHttpContext { TraceIdentifier = "req-1" };
        context.Request.Method = "POST";
        context.Request.Path = "/bookings";
        context.Response.Body = new MemoryStream();
        context.RequestAborted = aborted;
        return context;
    }

    private void VerifyLogged(LogLevel level, Exception? exception, Times times) =>
        _logger.Verify(l => l.Log(level, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), exception,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), times);

    [Fact]
    public async Task AnUnhandledException_Answers500WithAMessageAndTraceId_AndNeverTheExceptionText() {
        var context = NewContext();
        var error = new InvalidOperationException("connection string: Password=hunter2");

        var handled = await new GlobalExceptionHandler(_logger.Object).TryHandleAsync(context, error, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(500, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.DoesNotContain("hunter2", body);
        using var json = JsonDocument.Parse(body);
        Assert.Equal("Something went wrong. Please try again.", json.RootElement.GetProperty("message").GetString());
        Assert.Equal("req-1", json.RootElement.GetProperty("traceId").GetString());
        VerifyLogged(LogLevel.Error, error, Times.Once()); // the exception itself goes to the log
    }

    [Fact]
    public async Task AClientThatHungUp_IsNotAnError_AndGetsNoBody() {
        using var aborted = new CancellationTokenSource();
        await aborted.CancelAsync();
        var context = NewContext(aborted.Token);
        var error = new OperationCanceledException();

        var handled = await new GlobalExceptionHandler(_logger.Object).TryHandleAsync(context, error, CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(0, context.Response.Body.Length);
        VerifyLogged(LogLevel.Error, It.IsAny<Exception>(), Times.Never());
    }

    [Fact]
    public async Task ACancellationThatIsNotTheClients_IsStillAnError() {
        var context = NewContext(); // the request was not aborted: a timeout inside the server, say
        var error = new TaskCanceledException();

        await new GlobalExceptionHandler(_logger.Object).TryHandleAsync(context, error, CancellationToken.None);

        Assert.Equal(500, context.Response.StatusCode);
        VerifyLogged(LogLevel.Error, error, Times.Once());
    }
}
