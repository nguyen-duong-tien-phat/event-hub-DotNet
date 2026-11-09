using System.Text.Json;
using Amazon.SQS;
using EventHub.Core.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventHub.BackgroundJobs;

/// <summary>Sends the emails SqsEmailSender queued, through the "deliver" sender (which skips an email key already sent).</summary>
public class EmailQueueWorker(IAmazonSQS sqs, string queueUrl, IServiceScopeFactory scopes, ILogger logger)
    : QueueWorker(sqs, queueUrl, scopes, logger) {

    protected override async Task HandleAsync(string body, IServiceProvider services, CancellationToken ct) {
        var email = JsonSerializer.Deserialize<EventHub.Infrastructure.Email.QueuedEmail>(body)
            ?? throw new InvalidOperationException("The queued email is empty.");
        // "deliver", never the unkeyed sender: that one queues, so the email would go round forever.
        await services.GetRequiredKeyedService<IEmailSender>("deliver")
            .SendAsync(email.To, email.Subject, email.Body, email.Key);
    }
}
