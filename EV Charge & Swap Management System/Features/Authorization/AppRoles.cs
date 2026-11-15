namespace EVChargeSwap.Features.Authorization;

public static class AppRoles
{
    public const string Customer = "Customer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    public static Guid CustomerId { get; } = new("a56e1a51-b86d-4a39-99f0-22ee86bd65ef");
    public static Guid OperatorId { get; } = new("a21b7fc8-7c15-4548-b4d1-54d9af0e2739");
    public static Guid AdminId { get; } = new("345a3035-4f38-414a-bd80-33c820eb286b");

    public static IReadOnlySet<string> All { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            Customer,
            Operator,
            Admin
        };
}
