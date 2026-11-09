using System.Text.Json;
using Amazon.S3;
using Amazon.SQS;
using EventHub.BackgroundJobs;
using EventHub.Core.Bookings;
using EventHub.Core.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EventHub.Infrastructure.Queues;

public record PaymentRetry(string BookingId);
public record FileDelete(string Key);

/// <summary>A poster that could not be deleted from S3.</summary>
public interface IFileDeleteQueue {
    Task EnqueueAsync(string key);
}

public class SqsPaymentRetryQueue(IAmazonSQS sqs, string queueUrl) : IPaymentRetryQueue {
    public async Task EnqueueAsync(string bookingId) =>
        await sqs.SendMessageAsync(queueUrl, JsonSerializer.Serialize(new PaymentRetry(bookingId)));
}

public class SqsFileDeleteQueue(IAmazonSQS sqs, string queueUrl) : IFileDeleteQueue {
    public async Task EnqueueAsync(string key) =>
        await sqs.SendMessageAsync(queueUrl, JsonSerializer.Serialize(new FileDelete(key)));
}

/// <summary>Tries again to stop or refund the payment of a cancelled booking. Throws while Stripe still fails, so the queue redelivers.</summary>
public class PaymentRetryWorker(IAmazonSQS sqs, string queueUrl, IServiceScopeFactory scopes, ILogger logger)
    : QueueWorker(sqs, queueUrl, scopes, logger) {

    protected override async Task HandleAsync(string body, IServiceProvider services, CancellationToken ct) {
        var retry = JsonSerializer.Deserialize<PaymentRetry>(body) ?? throw new InvalidOperationException("The message is empty.");
        await services.GetRequiredService<BookingService>().RetryStopPaymentAsync(retry.BookingId);
    }
}

/// <summary>Deletes the poster again. Deleting a file that is already gone succeeds, so a repeat is harmless.</summary>
public class FileDeleteWorker(IAmazonSQS sqs, string queueUrl, IServiceScopeFactory scopes, ILogger logger, IAmazonS3 s3, string bucket)
    : QueueWorker(sqs, queueUrl, scopes, logger) {

    protected override async Task HandleAsync(string body, IServiceProvider services, CancellationToken ct) {
        var file = JsonSerializer.Deserialize<FileDelete>(body) ?? throw new InvalidOperationException("The message is empty.");
        await s3.DeleteObjectAsync(bucket, file.Key, ct);
    }
}
