namespace TimeOps.Domain;

public enum ErrorCategory { Validation, NotFound, Authentication, Authorization, Unavailable, Incomplete, Unexpected }

public sealed record Error(string Code, ErrorCategory Category, string Message);

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess == (error is not null))
            throw new ArgumentException("O resultado deve conter sucesso ou erro.");
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    private Result(bool isSuccess, T? value, Error? error) : base(isSuccess, error) => _value = value;

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException("Falha não possui valor.");

    public static Result<T> Success(T value) => new(true, value, null);
    public new static Result<T> Failure(Error error) => new(false, default, error ?? throw new ArgumentNullException(nameof(error)));
}
