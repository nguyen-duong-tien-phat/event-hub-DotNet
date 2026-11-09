using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHub.BackgroundJobs;

/// <summary>
/// Reads one SQS queue and handles its messages one by one. SQS only hides a received message for the visibility
/// timeout; it is deleted here, after <see cref="HandleAsync"/> succeeded. A handler that throws leaves it, so SQS
/// delivers it again, and after the queue's maxReceiveCount tries moves it to the dead-letter queue.
/// Delivery is at least once, so a handler must be safe to run twice for the same message.
/// </summary>
public abstract class QueueWorker(IAmazonSQS sqs, string queueUrl, IServiceScopeFactory scopes, ILogger logger) : BackgroundService {
    /// <summary>Handles one message. Gets a scope of its own, so it can use scoped services such as the DbContext.</summary>
    protected abstract Task HandleAsync(string body, IServiceProvider services, CancellationToken ct);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20   // long polling: SQS holds the call until a message arrives
                }, stoppingToken);

                foreach (var message in received.Messages ?? []) {   // null, not empty, when the wait ran out
                    try {
                        using var scope = scopes.CreateScope();
                        await HandleAsync(message.Body, scope.ServiceProvider, stoppingToken);
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) {
                        logger.LogError(ex, "Handling message {MessageId} of {Queue} failed; it will be retried",
                            message.MessageId, queueUrl);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError(ex, "Reading the queue {Queue} failed", queueUrl);
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}
