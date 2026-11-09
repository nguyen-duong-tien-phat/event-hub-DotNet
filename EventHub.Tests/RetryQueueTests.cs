using System.Text.Json;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.SQS;
using Amazon.SQS.Model;
using EventHub.BackgroundJobs;
using EventHub.Core.Bookings;
using EventHub.Core.Common;
using EventHub.Core.Events;
using EventHub.Core.Payments;
using EventHub.Core.Tickets;
using EventHub.Core.Users;
using EventHub.Infrastructure.Files;
using EventHub.Infrastructure.Queues;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace EventHub.Tests;

public class RetryQueueTests {
    private readonly Mock<IBookingRepository> _bookings = new();
    private readonly Mock<IPaymentService> _payment = new();
    private readonly Mock<IPaymentRetryQueue> _retry = new();
    private readonly Mock<IAmazonSQS> _sqs = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Booking _booking = new() {
        EventId = "event_1", UserId = "user_1", Status = BookingStatus.Pending, PaymentIntentId = "pi_1",
        Tickets = [new BookingTicket { TicketId = "t1", Quantity = 1, UnitPrice = 25m }],
    };

    public RetryQueueTests() {
        _bookings.Setup(r => r.GetByIdAsync(_booking.Id)).ReturnsAsync(_booking);
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ThrowsAsync(new InvalidOperationException("stripe down"));
    }

    private BookingService Service(bool withQueue = true) => new(
        _bookings.Object, new Mock<ITicketRepository>().Object, new Mock<IEventRepository>().Object,
        new Mock<IUnitOfWork>().Object, _payment.Object, null, null, null, withQueue ? _retry.Object : null);

    [Fact]
    public async Task CancelAsync_WhenStripeFails_QueuesARetry_InsteadOfFailing() {
        var result = await Service().CancelAsync(_booking.Id, "user_1", UserRole.Attendee);

        Assert.Equal(BookingStatus.Cancelled, result!.Status);
        _retry.Verify(q => q.EnqueueAsync(_booking.Id), Times.Once);
    }

    [Fact]
    public async Task CancelAsync_WhenStripeFails_AndThereIsNoQueue_StillThrows() {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(withQueue: false).CancelAsync(_booking.Id, "user_1", UserRole.Attendee));
    }

    [Fact]
    public async Task ExpireAbandonedBooking_WhenStripeFails_QueuesARetry_AndReportsNoFailure() {
        _bookings.Setup(r => r.GetExpiredPendingBookingsAsync(It.IsAny<DateTime>())).ReturnsAsync([_booking]);

        var result = await Service().ExpireAbandonedBookingAsync(TimeSpan.FromMinutes(15));

        Assert.Equal(1, result.Expired);
        Assert.Empty(result.Failures);
        _retry.Verify(q => q.EnqueueAsync(_booking.Id), Times.Once);
    }

    [Fact]
    public async Task RetryStopPayment_OnACancelledBooking_StopsThePayment() {
        _booking.Status = BookingStatus.Cancelled;
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(true);

        await Service().RetryStopPaymentAsync(_booking.Id);

        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_1"), Times.Once);
    }

    [Fact]
    public async Task RetryStopPayment_OnABookingThatIsNotCancelled_DoesNothing() {
        await Service().RetryStopPaymentAsync(_booking.Id);

        _payment.Verify(p => p.CancelPaymentIntentAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RetryStopPayment_WhenStripeStillFails_Throws_SoTheQueueDeliversItAgain() {
        _booking.Status = BookingStatus.Cancelled;

        await Assert.ThrowsAsync<InvalidOperationException>(() => Service().RetryStopPaymentAsync(_booking.Id));
    }

    [Fact]
    public async Task SqsPaymentRetryQueue_QueuesTheBookingId() {
        string? body = null;
        _sqs.Setup(s => s.SendMessageAsync("url", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((_, b, _) => body = b)
            .ReturnsAsync(new SendMessageResponse());

        await new SqsPaymentRetryQueue(_sqs.Object, "url").EnqueueAsync("booking_1");

        Assert.Equal(new PaymentRetry("booking_1"), JsonSerializer.Deserialize<PaymentRetry>(body!));
    }

    [Fact]
    public async Task PaymentRetryWorker_RetriesTheBooking_ThenDeletesTheMessage() {
        _booking.Status = BookingStatus.Cancelled;
        _payment.Setup(p => p.CancelPaymentIntentAsync("pi_1")).ReturnsAsync(true);
        var services = new ServiceCollection().AddSingleton(Service());
        Receive(new PaymentRetry(_booking.Id));

        await RunAsync(new PaymentRetryWorker(_sqs.Object, "url", Scopes(services), NullLogger.Instance));

        _payment.Verify(p => p.CancelPaymentIntentAsync("pi_1"), Times.Once);
        _sqs.Verify(s => s.DeleteMessageAsync("url", "handle", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PaymentRetryWorker_WhenStripeStillFails_KeepsTheMessage() {
        _booking.Status = BookingStatus.Cancelled;
        var services = new ServiceCollection().AddSingleton(Service());
        Receive(new PaymentRetry(_booking.Id));

        await RunAsync(new PaymentRetryWorker(_sqs.Object, "url", Scopes(services), NullLogger.Instance));

        _sqs.Verify(s => s.DeleteMessageAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task FileDeleteWorker_DeletesTheFile_ThenTheMessage() {
        var s3 = new Mock<IAmazonS3>();
        Receive(new FileDelete("events/images/a.png"));

        await RunAsync(new FileDeleteWorker(_sqs.Object, "url", Scopes(new ServiceCollection()), NullLogger.Instance, s3.Object, "bucket"));

        s3.Verify(c => c.DeleteObjectAsync("bucket", "events/images/a.png", It.IsAny<CancellationToken>()), Times.Once);
        _sqs.Verify(s => s.DeleteMessageAsync("url", "handle", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task S3FileStorage_WhenTheDeleteFails_QueuesIt_AndDoesNotThrow() {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(c => c.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonClientException("down"));
        var queue = new Mock<IFileDeleteQueue>();

        await new S3FileStorage(s3.Object, "bucket", "http://files.test/bucket", NullLogger<S3FileStorage>.Instance, queue.Object)
            .DeleteAsync("events/images/a.png");

        queue.Verify(q => q.EnqueueAsync("events/images/a.png"), Times.Once);
    }

    [Fact]
    public async Task S3FileStorage_WhenTheQueueFailsToo_StillDoesNotThrow() {
        var s3 = new Mock<IAmazonS3>();
        s3.Setup(c => c.DeleteObjectAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonClientException("down"));
        var queue = new Mock<IFileDeleteQueue>();
        queue.Setup(q => q.EnqueueAsync(It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("queue down"));

        await new S3FileStorage(s3.Object, "bucket", "http://files.test/bucket", NullLogger<S3FileStorage>.Instance, queue.Object)
            .DeleteAsync("events/images/a.png");
    }

    private static IServiceScopeFactory Scopes(IServiceCollection services) =>
        services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

    /// <summary>One message on the first read, and the worker is asked to stop, so its loop ends after handling it.</summary>
    private void Receive<T>(T message) =>
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => _stop.Cancel())
            .ReturnsAsync(new ReceiveMessageResponse {
                Messages = [new Message { MessageId = "m1", ReceiptHandle = "handle", Body = JsonSerializer.Serialize(message) }]
            });

    private async Task RunAsync(QueueWorker worker) {
        await worker.StartAsync(_stop.Token);
        await worker.ExecuteTask!;
    }
}
