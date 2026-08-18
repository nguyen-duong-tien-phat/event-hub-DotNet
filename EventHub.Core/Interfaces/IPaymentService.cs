namespace EventHub.Core.Interfaces;

public class PaymentIntentResult {
    public string PaymentIntentId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } =  string.Empty;
}

public interface IPaymentService {
    Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, Guid bookingId);
}