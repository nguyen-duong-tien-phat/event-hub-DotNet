using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using EventHub.Core.Email;
using EventHub.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace EventHub.BackgroundJobs;

public class EmailQueueWorker(
    IAmazonSQS sqs, 
    string queueUrl, 
    IServiceScopeFactory scopes, 
    ILogger logger): BackgroundService {
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
        while (!stoppingToken.IsCancellationRequested) {
            try {
                var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = 10,
                    WaitTimeSeconds = 20
                }, stoppingToken);

                foreach (var message in received.Messages ?? []) {
                    try {
                        var email = JsonSerializer.Deserialize<QueuedEmail>(message.Body);
                        
                        using var scope = scopes.CreateScope();
                        var deliverer = scope.ServiceProvider.GetRequiredKeyedService<IEmailSender>("deliver");
                        await deliverer.SendAsync(email.To, email.Subject, email.Body, email.Key);
                        await sqs.DeleteMessageAsync(queueUrl, message.ReceiptHandle, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) {                
                        logger.LogError(
                            ex, 
                            "Sending queued email {MessageId} failed; it will be retried", 
                            message.MessageId);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) {
                logger.LogError(ex, "Reading the email queue failed");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}