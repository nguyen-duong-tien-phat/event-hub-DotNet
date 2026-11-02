namespace EventHub.Core.Payments;

public class PaymentIntentResult {
    public string PaymentIntentId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } =  string.Empty;
}

public interface IPaymentService {
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, string bookingId);

    /// <summary>The client secret of an existing payment intent, so a replayed request can hand it back.</summary>
    Task<string> GetClientSecretAsync(string paymentIntentId);

    /// <summary>True when Stripe reports the payment as succeeded.</summary>
    Task<bool> IsPaymentSucceededAsync(string paymentIntentId);
    
    /// <summary>Cancels an unpaid intent so Stripe refuses any later payment. False if it can't be cancelled (e.g. already paid).</summary>
    Task<bool> CancelPaymentIntentAsync(string paymentIntentId);

    /// <summary>Gives the money of a paid intent back, once however many times it is called.</summary>
    Task RefundAsync(string paymentIntentId, string bookingId);
}