using System.Net;
using System.Net.Http.Json;
using EVChargeSwap.Features.Reservations;
using Microsoft.AspNetCore.Hosting;
using Xunit;

namespace EVChargeSwap.Tests.Integration;

public sealed class PaymentWebhookApiTests
{
    [Fact]
    public async Task A_signature_over_a_different_amount_is_rejected_before_any_payment_lookup()
    {
        const string secret = "integration-payment-secret-at-least-32-characters";
        using var source = new ApiFactory();
        using var factory = source.WithWebHostBuilder(builder => builder.UseSetting("MockPayments:WebhookSecret", secret));
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var original = new MockPaymentEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 25000m, true);
        client.DefaultRequestHeaders.Add("X-EVGo-Signature", PaymentsController.Sign(original, secret));
        var response = await client.PostAsJsonAsync("/api/payments/mock/webhook", original with { Amount = 1m });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
