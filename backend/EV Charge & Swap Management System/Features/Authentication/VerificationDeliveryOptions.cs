namespace EVChargeSwap.Features.Authentication;

public sealed class VerificationDeliveryOptions
{
    public const string SectionName = "VerificationDelivery";

    public string ResendApiKey { get; init; } = string.Empty;
    public string EmailFrom { get; init; } = string.Empty;
    public string TwilioAccountSid { get; init; } = string.Empty;
    public string TwilioAuthToken { get; init; } = string.Empty;
    public string TwilioSenderId { get; init; } = string.Empty;
}
