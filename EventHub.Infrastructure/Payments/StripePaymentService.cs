using EventHub.Core.Interfaces;
using Stripe;

namespace EventHub.Infrastructure.Payments;

public class StripePaymentService: IPaymentService {
    public async Task<PaymentIntentResult> CreatePaymentIntentAsync(decimal amount, string currency, Guid bookingId) {
        var amountInSmallestUnit = (long)(amount * 100);

        var options = new PaymentIntentCreateOptions {
            Amount = amountInSmallestUnit,
            Currency = currency,
            Metadata = new Dictionary<string, string> {
                {"bookingId", bookingId.ToString()}
            }
        };

        var requestOptions = new RequestOptions {
            IdempotencyKey = bookingId.ToString(),
        };

        var service = new PaymentIntentService();
        var intent = await service.CreateAsync(options, requestOptions);
        return new PaymentIntentResult {
            PaymentIntentId = intent.Id,
            ClientSecret = intent.ClientSecret
        };

    }
}