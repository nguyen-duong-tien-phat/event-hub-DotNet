namespace EventHub.Core.Payments;

public class PaymentIntentResult {
    public string PaymentIntentId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } =  string.Empty;
}

public interface IPaymentService {
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, string bookingId);

    /// <summary>True when Stripe reports the payment as succeeded.</summary>
    Task<bool> IsPaymentSucceededAsync(string paymentIntentId);
}