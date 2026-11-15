namespace EVChargeSwap.Features.Authentication.Services;

public interface IVerificationCodeSender
{
    Task SendAsync(string channel, string destination, string code, CancellationToken cancellationToken);
}

// Local adapter. Replace with email and SMS providers before sending real messages.
public sealed class DevelopmentCodeInbox : IVerificationCodeSender
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> codes = new();

    public Task SendAsync(string channel, string destination, string code, CancellationToken cancellationToken)
    {
        codes[$"{channel}:{destination}"] = code;
        return Task.CompletedTask;
    }

    public string? GetCode(string channel, string destination) =>
        codes.TryGetValue($"{channel}:{destination}", out var code) ? code : null;
}

public sealed class UnconfiguredVerificationSender : IVerificationCodeSender
{
    public Task SendAsync(string channel, string destination, string code, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Configure an email and SMS provider before enabling registration in Production.");
}
