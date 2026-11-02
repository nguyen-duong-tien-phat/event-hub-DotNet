using Stripe;

namespace EventHub.Core.Payments;

public class StripePaymentService: IPaymentService {
    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, string bookingId) {
        var amountInSmallestUnit = (long)(amount * 100);

        var options = new PaymentIntentCreateOptions {
            Amount = amountInSmallestUnit,
            Currency = currency,
            Metadata = new Dictionary<string, string> {
                {"bookingId", bookingId.ToString()}
            },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions {
                Enabled = true,
                AllowRedirects = "never"
            }
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options);
        return new PaymentIntentResult {
            PaymentIntentId = intent.Id,
            ClientSecret = intent.ClientSecret
        };

    }

    public async Task<string> GetClientSecretAsync(string paymentIntentId) {
        var intent = await new PaymentIntentService().GetAsync(paymentIntentId);
        return intent.ClientSecret;
    }

    public async Task<bool> IsPaymentSucceededAsync(string paymentIntentId) {
        var intent = await new PaymentIntentService().GetAsync(paymentIntentId);
        return intent.Status == "succeeded";
    }

    public async Task<bool> CancelPaymentIntentAsync(string paymentIntentId) {
        try {
            await new PaymentIntentService().CancelAsync(paymentIntentId);
            return true;
        } catch (StripeException e) {
            return false;
        }
    }

    public async Task RefundAsync(string paymentIntentId, string bookingId) {
        await new RefundService().CreateAsync(
            new RefundCreateOptions{PaymentIntent = paymentIntentId},
            new RequestOptions {IdempotencyKey = $"refund-{bookingId}"});
    }
}
