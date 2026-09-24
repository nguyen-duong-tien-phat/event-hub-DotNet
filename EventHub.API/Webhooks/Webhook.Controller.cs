using EventHub.Core.Bookings;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace EventHub.Webhooks;

[ApiController]
[Route("/webhooks")]
public class WebhooksController(
    BookingService bookingService, 
    IConfiguration config,
    ILogger<WebhooksController> logger): ControllerBase 
{
    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook() {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"];
        var webhookSecret = config["Stripe:WebhookSecret"];

        Event stripeEvent;
        try {
            stripeEvent = EventUtility.ConstructEvent(json, signature, webhookSecret);
        }
        catch {
            return BadRequest(); // signature invalid - reject
        }
        
        if (stripeEvent.Type == EventTypes.PaymentIntentSucceeded) {
            var intent = (PaymentIntent)stripeEvent.Data.Object;
            await bookingService.HandlePaymentSucceeded(intent.Id);
        }
        else if (stripeEvent.Type == EventTypes.PaymentIntentPaymentFailed) {
            var intent = (PaymentIntent)stripeEvent.Data.Object;
            await bookingService.HandlePaymentFailed(intent.Id);
        }
        else {
            logger.LogInformation("Unhandled event type: {EventType}", stripeEvent.Type);
        }
        
        return Ok();
    }
}