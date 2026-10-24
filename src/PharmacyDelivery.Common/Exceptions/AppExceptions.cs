using PharmacyDelivery.Common.Errors;

namespace PharmacyDelivery.Common.Exceptions;

/// <summary>
/// Base class of application errors. The API maps each subtype to an HTTP status and returns <see cref="Code"/>
/// in ProblemDetails; see DESIGN.md §13 for the full mapping.
/// </summary>
public abstract class AppException(string code, string message, Exception? inner = null) : Exception(message, inner)
{
    public string Code { get; } = code;
}

/// <summary>Resource does not exist or belongs to someone else (we never reveal which) — 404.</summary>
public sealed class NotFoundException(string code, string message) : AppException(code, message);

/// <summary>Authenticated, but not allowed to perform the action — 403.</summary>
public sealed class ForbiddenException(string code, string message) : AppException(code, message);

/// <summary>Credentials or token are invalid — 401.</summary>
public sealed class UnauthorizedException(string code, string message) : AppException(code, message);

/// <summary>Business rule of a use case is not satisfied (no pharmacy in range, empty cart…) — 422.</summary>
public sealed class BusinessRuleException(string code, string message) : AppException(code, message);

/// <summary>Conflicting concurrent change or unique constraint — 409; the client may retry.</summary>
public sealed class ConflictException(string code, string message, Exception? inner = null) : AppException(code, message, inner);

/// <summary>Input validation failed — 400 with a dictionary of field errors.</summary>
public sealed class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors, string message = "Validation failed.")
    : AppException(ErrorCodes.ValidationFailed, message)
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static ValidationFailedException ForField(string field, string code, string message) =>
        new(new Dictionary<string, string[]> { [field] = [code + ": " + message] }, message);
}

/// <summary>A dependency (database, cache) is unavailable after retries — 503 with Retry-After.</summary>
public sealed class ServiceUnavailableException(string message, Exception? inner = null)
    : AppException(ErrorCodes.ServiceUnavailable, message, inner);

/// <summary>A database statement exceeded its timeout — 504.</summary>
public sealed class DatabaseTimeoutException(string message, Exception? inner = null)
    : AppException(ErrorCodes.DatabaseTimeout, message, inner);
