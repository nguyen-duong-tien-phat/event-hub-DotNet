using System.Text.Json;
using Amazon.SQS;
using EventHub.Core.Email;

namespace EventHub.Infrastructure.Email;

public record QueuedEmail(string To, string Subject, string Body, string Key);

/// <summary>Queues the email on SQS instead of sending it; EmailQueueWorker delivers it.</summary>
public class SqsEmailSender(IAmazonSQS sqs, string queueUrl) : IEmailSender {
    public async Task SendAsync(string to, string subject, string body, string key) =>
        await sqs.SendMessageAsync(queueUrl, JsonSerializer.Serialize(new QueuedEmail(to, subject, body, key)));
}