namespace GUNRPG.Application.Results;

/// <summary>
/// Discriminated status for service results to enable pattern matching.
/// </summary>
public enum ResultStatus
{
    Success,
    NotFound,
    InvalidState,
    ValidationError
}

/// <summary>
/// Represents the outcome of a service operation with explicit success/failure states.
/// </summary>
public abstract class ServiceResultBase
{
    public bool IsSuccess { get; }
    public ResultStatus Status { get; }
    public string? ErrorMessage { get; }

    protected ServiceResultBase(bool isSuccess, ResultStatus status, string? errorMessage = null)
    {
        IsSuccess = isSuccess;
        Status = status;
        ErrorMessage = errorMessage;
    }
}

/// <summary>
/// Service result that carries a value on success.
/// </summary>
public sealed class ServiceResult<T> : ServiceResultBase
{
    public T? Value { get; }

    private ServiceResult(bool isSuccess, ResultStatus status, T? value, string? errorMessage)
        : base(isSuccess, status, errorMessage)
    {
        Value = value;
    }

    public static ServiceResult<T> Success(T value) => new(true, ResultStatus.Success, value, null);
    public static ServiceResult<T> NotFound(string? message = null) => new(false, ResultStatus.NotFound, default, message ?? "Resource not found");
    public static ServiceResult<T> InvalidState(string message) => new(false, ResultStatus.InvalidState, default, message);
    public static ServiceResult<T> ValidationError(string message) => new(false, ResultStatus.ValidationError, default, message);
    
    /// <summary>
    /// Creates a ServiceResult&lt;T&gt; from a non-generic ServiceResult, preserving the error state.
    /// This should only be used to convert error results; Success results should use Success(T value) directly.
    /// </summary>
    public static ServiceResult<T> FromResult(ServiceResult result)
    {
        return result.Status switch
        {
            ResultStatus.Success => throw new InvalidOperationException("Cannot convert Success result without a value. Use Success(T value) instead."),
            ResultStatus.NotFound => NotFound(result.ErrorMessage),
            ResultStatus.InvalidState => InvalidState(result.ErrorMessage!),
            ResultStatus.ValidationError => ValidationError(result.ErrorMessage!),
            _ => InvalidState(result.ErrorMessage!)
        };
    }
}

/// <summary>
/// Service result without a value.
/// </summary>
public sealed class ServiceResult : ServiceResultBase
{
    private ServiceResult(bool isSuccess, ResultStatus status, string? errorMessage)
        : base(isSuccess, status, errorMessage)
    {
    }

    public static ServiceResult Success() => new(true, ResultStatus.Success, null);
    public static ServiceResult NotFound(string? message = null) => new(false, ResultStatus.NotFound, message ?? "Resource not found");
    public static ServiceResult InvalidState(string message) => new(false, ResultStatus.InvalidState, message);
    public static ServiceResult ValidationError(string message) => new(false, ResultStatus.ValidationError, message);
}
