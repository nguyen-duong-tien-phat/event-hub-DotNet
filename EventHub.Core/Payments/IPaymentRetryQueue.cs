namespace EventHub.Core.Payments;

/// <summary>Where a cancelled booking goes when stopping or refunding its payment failed, to be tried again.</summary>
public interface IPaymentRetryQueue {
    Task EnqueueAsync(string bookingId);
}
