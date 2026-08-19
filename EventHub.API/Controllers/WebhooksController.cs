using EventHub.Core.Enums;
using EventHub.Core.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Stripe;

namespace EventHub.Controllers;

[ApiController]
[Route("/webhooks")]
public class WebhooksController(IBookingRepository bookingRepository, ITicketRepository ticketRepository, IConfiguration config): ControllerBase {
    [HttpPost("stripe")]
    public async Task<IActionResult> StripeWebhook() {
        var json = await new StreamReader(Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"];
        var webhookSecret = config["Stripe:WebhookSecret"];

        Event stripeEvent;
        try {
            stripeEvent = EventUtility.ConstructEvent(json, signature, webhookSecret);
        }
        catch (StripeException e) {
            return BadRequest(); // signature invalid - reject
        }
        
        if (stripeEvent.Type == EventTypes.PaymentIntentSucceeded) {
            var intent = (PaymentIntent)stripeEvent.Data.Object;
            await HandlePaymentSucceeded(intent.Id);
        }
        else if (stripeEvent.Type == EventTypes.PaymentIntentPaymentFailed) {
            var intent = (PaymentIntent)stripeEvent.Data.Object;
            await HandlePaymentFailed(intent.Id, intent.Metadata);
        }
        else {
            Console.WriteLine("Unhandled event type: {0}", stripeEvent.Type);
        }
        
        return Ok();
    }

    private async Task HandlePaymentSucceeded(string paymentIntentId) {
        var booking = await bookingRepository.GetByPaymentIntentId(paymentIntentId);
        if (booking == null || booking.Status != BookingStatus.Pending) {
            return;
        }

        booking.Status = BookingStatus.Confirmed;
        bookingRepository.Update(booking);
        await bookingRepository.SaveChangesAsync();
    }

    private async Task HandlePaymentFailed(string paymentIntentId, Dictionary<string, string> metadata) {
        var booking = await bookingRepository.GetByPaymentIntentId(paymentIntentId);
        if (booking == null || booking.Status != BookingStatus.Pending) {
            return;
        }
        
        booking.Status = BookingStatus.Cancelled;
        bookingRepository.Update(booking);
        await ticketRepository.ReleaseAsync(booking.TicketId, booking.Quantity);
        await bookingRepository.SaveChangesAsync();
    }

}