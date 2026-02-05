namespace GUNRPG.Application.Results;

/// <summary>
/// Represents the outcome of a service operation with explicit success/failure states.
/// </summary>
public abstract class ServiceResultBase
{
    public bool IsSuccess { get; }
    public string? ErrorMessage { get; }

    protected ServiceResultBase(bool isSuccess, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Service result that carries a value on success.
/// </summary>
public sealed class ServiceResult<T> : ServiceResultBase
{
    public T? Value { get; }

    private ServiceResult(bool isSuccess, T? value, string? errorMessage)
        : base(isSuccess, errorMessage)
    {
        Value = value;
    }

    public static ServiceResult<T> Success(T value) => new(true, value, null);
    public static ServiceResult<T> NotFound(string? message = null) => new(false, default, message ?? "Resource not found");
    public static ServiceResult<T> InvalidState(string message) => new(false, default, message);
    public static ServiceResult<T> ValidationError(string message) => new(false, default, message);
}

/// <summary>
/// Service result without a value.
/// </summary>
public sealed class ServiceResult : ServiceResultBase
{
    private ServiceResult(bool isSuccess, string? errorMessage)
        : base(isSuccess, errorMessage)
    {
    }

    public static ServiceResult Success() => new(true, null);
    public static ServiceResult NotFound(string? message = null) => new(false, message ?? "Resource not found");
    public static ServiceResult InvalidState(string message) => new(false, message);
    public static ServiceResult ValidationError(string message) => new(false, message);
}
