namespace EVChargeSwap.Features.Auth;

public enum AuthFailure
{
    None,
    Validation,
    Conflict,
    Unauthorized,
    NotFound
}

public sealed class AuthResult<T>
{
    private AuthResult(T? value, AuthFailure failure, string? detail,
        IReadOnlyDictionary<string, string[]> errors)
    {
        Value = value;
        Failure = failure;
        Detail = detail;
        Errors = errors;
    }

    public T? Value { get; }
    public AuthFailure Failure { get; }
    public string? Detail { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public bool Succeeded => Failure == AuthFailure.None;

    public static AuthResult<T> Success(T value) =>
        new(value, AuthFailure.None, null, new Dictionary<string, string[]>());

    public static AuthResult<T> Fail(AuthFailure failure, string detail,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(default, failure, detail, errors ?? new Dictionary<string, string[]>());
}
