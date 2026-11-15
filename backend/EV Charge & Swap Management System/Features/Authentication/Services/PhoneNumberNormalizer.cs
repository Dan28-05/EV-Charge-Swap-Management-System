namespace EVChargeSwap.Features.Authentication.Services;

// The first release serves Vietnamese phone numbers. Extend this at the country rollout boundary.
public static class PhoneNumberNormalizer
{
    public static string? Normalize(string input)
    {
        var value = input.Trim().Replace(" ", "").Replace("-", "");
        if (value.StartsWith("0", StringComparison.Ordinal)) value = "+84" + value[1..];
        else if (value.StartsWith("84", StringComparison.Ordinal)) value = "+" + value;
        return System.Text.RegularExpressions.Regex.IsMatch(value, @"^\+84[35789][0-9]{8}$") ? value : null;
    }
}
