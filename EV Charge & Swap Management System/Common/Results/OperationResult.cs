namespace EVChargeSwap.Common.Results;

public enum OperationFailure
{
    None,
    Validation,
    Conflict,
    Unauthorized,
    NotFound
}

public sealed class OperationResult<T>
{
    private OperationResult(T? value, OperationFailure failure, string? detail,
        IReadOnlyDictionary<string, string[]> errors)
    {
        Value = value;
        Failure = failure;
        Detail = detail;
        Errors = errors;
    }

    public T? Value { get; }
    public OperationFailure Failure { get; }
    public string? Detail { get; }
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public bool Succeeded => Failure == OperationFailure.None;

    public static OperationResult<T> Success(T value) =>
        new(value, OperationFailure.None, null, new Dictionary<string, string[]>());

    public static OperationResult<T> Fail(OperationFailure failure, string detail,
        IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(default, failure, detail, errors ?? new Dictionary<string, string[]>());
}
