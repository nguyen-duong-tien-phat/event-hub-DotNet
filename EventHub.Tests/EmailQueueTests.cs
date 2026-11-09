using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using EventHub.BackgroundJobs;
using EventHub.Core.Email;
using EventHub.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EventHub.Tests;

public class EmailQueueTests {
    private const string QueueUrl = "http://queue/eventhub-email";
    private readonly Mock<IAmazonSQS> _sqs = new();
    private readonly Mock<IEmailSender> _deliverer = new();
    private readonly CancellationTokenSource _stop = new();

    [Fact]
    public async Task SqsEmailSender_QueuesTheEmailAsJson_InsteadOfSendingIt() {
        string? body = null;
        _sqs.Setup(s => s.SendMessageAsync(QueueUrl, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, b, _) => body = b)
            .ReturnsAsync(new SendMessageResponse());

        await new SqsEmailSender(_sqs.Object, QueueUrl).SendAsync("a@b.c", "subject", "body", "k1");

        Assert.Equal(new QueuedEmail("a@b.c", "subject", "body", "k1"), JsonSerializer.Deserialize<QueuedEmail>(body!));
    }

    [Fact]
    public async Task Worker_DeliversAMessage_ThenDeletesIt() {
        ReceiveOneMessage();

        await RunWorkerAsync();

        _deliverer.Verify(e => e.SendAsync("a@b.c", "subject", "body", "k1"), Times.Once);
        _sqs.Verify(s => s.DeleteMessageAsync(QueueUrl, "handle-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Worker_WhenDeliveryFails_KeepsTheMessageSoTheQueueRetriesIt() {
        _deliverer.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));
        ReceiveOneMessage();

        await RunWorkerAsync();

        _sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Worker_WhenReadingTheQueueFails_KeepsRunning() {
        // First read throws, the loop waits 5 s and reads again (and stops then); one test pays that wait.
        var reads = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => {
                if (++reads == 1) throw new AmazonSQSException("boom");
                _stop.Cancel();
                return new ReceiveMessageResponse { Messages = [] };
            });

        await RunWorkerAsync();

        Assert.Equal(2, reads);
    }

    /// <summary>The first read returns one message and asks the worker to stop, so the loop ends after it.</summary>
    private void ReceiveOneMessage() =>
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => _stop.Cancel())
            .ReturnsAsync(new ReceiveMessageResponse {
                Messages = [new Message {
                    MessageId = "m1",
                    ReceiptHandle = "handle-1",
                    Body = JsonSerializer.Serialize(new QueuedEmail("a@b.c", "subject", "body", "k1"))
                }]
            });

    private async Task RunWorkerAsync() {
        // The worker asks for the "deliver" sender (the dedup decorator), not the raw SMTP one.
        var services = new ServiceCollection();
        services.AddKeyedSingleton<IEmailSender>("deliver", _deliverer.Object);
        var scopes = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        var worker = new EmailQueueWorker(_sqs.Object, QueueUrl, scopes, NullLogger.Instance);
        await worker.StartAsync(_stop.Token);
        await worker.ExecuteTask!;
    }
}
