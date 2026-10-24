using System.Diagnostics;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using PharmacyDelivery.Common.Errors;
using PharmacyDelivery.Common.Exceptions;
using PharmacyDelivery.Domain.Common;

namespace PharmacyDelivery.Api.Infrastructure;

public sealed class ErrorHandlingOptions
{
    public const string SectionName = "ErrorHandling";

    /// <summary>Include exception type and stack trace in error responses. Development only.</summary>
    public bool IncludeExceptionDetails { get; set; }
}

/// <summary>
/// Maps exceptions to RFC 9457 ProblemDetails (DESIGN.md §13). Every response carries <c>code</c> (stable,
/// machine-readable) and <c>traceId</c> (the OpenTelemetry trace id — paste it into the trace UI to find the request).
/// </summary>
public sealed class PharmacyDeliveryExceptionHandler(
    IProblemDetailsService problemDetails,
    IOptions<ErrorHandlingOptions> options,
    ILogger<PharmacyDeliveryExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // Client closed the connection: nothing to answer, the database work was cancelled with the request.
            httpContext.Response.StatusCode = 499;
            return true;
        }

        var (status, code, detail) = Map(exception);
        if (status >= 500)
            logger.LogError(exception, "Request failed with {StatusCode} {Code}", status, code);
        else
            logger.LogWarning("Request rejected with {StatusCode} {Code}: {Detail}", status, code, detail);

        var problem = status == StatusCodes.Status400BadRequest && exception is ValidationFailedException validation
            ? new ValidationProblemDetails(validation.Errors.ToDictionary(e => e.Key, e => e.Value)) { Detail = detail }
            : new ProblemDetails { Detail = detail };
        problem.Status = status;
        problem.Title = ReasonPhrases(status);
        problem.Type = "https://www.rfc-editor.org/rfc/rfc9110#status." + status;
        problem.Extensions["code"] = code;
        if (options.Value.IncludeExceptionDetails)
            problem.Extensions["exception"] = exception.ToString();

        if (exception is ServiceUnavailableException)
            httpContext.Response.Headers.RetryAfter = "5";

        httpContext.Response.StatusCode = status;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });
    }

    public static (int Status, string Code, string Detail) Map(Exception exception) => exception switch
    {
        ValidationFailedException e => (StatusCodes.Status400BadRequest, e.Code, e.Message),
        ValidationException e => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, e.Message),
        BadHttpRequestException e => (StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, e.Message),
        UnauthorizedException e => (StatusCodes.Status401Unauthorized, e.Code, e.Message),
        ForbiddenException e => (StatusCodes.Status403Forbidden, e.Code, e.Message),
        NotFoundException e => (StatusCodes.Status404NotFound, e.Code, e.Message),
        ConflictException e => (StatusCodes.Status409Conflict, e.Code, e.Message),
        BusinessRuleException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
        DomainException e => (StatusCodes.Status422UnprocessableEntity, e.Code, e.Message),
        ServiceUnavailableException e => (StatusCodes.Status503ServiceUnavailable, e.Code,
            "A required service is temporarily unavailable. Retry later."),
        DatabaseTimeoutException e => (StatusCodes.Status504GatewayTimeout, e.Code, "The operation timed out."),
        _ => (StatusCodes.Status500InternalServerError, ErrorCodes.Internal, "An unexpected error occurred."),
    };

    private static string ReasonPhrases(int status) => Microsoft.AspNetCore.WebUtilities.ReasonPhrases.GetReasonPhrase(status);
}

/// <summary>Adds <c>traceId</c> to every ProblemDetails, including those produced by the framework (401, 404, 415…).</summary>
public static class ProblemDetailsCustomization
{
    public static void Customize(ProblemDetailsContext context)
    {
        context.ProblemDetails.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
        if (!context.ProblemDetails.Extensions.ContainsKey("code"))
        {
            context.ProblemDetails.Extensions["code"] = context.ProblemDetails.Status switch
            {
                StatusCodes.Status400BadRequest => ErrorCodes.ValidationFailed,
                StatusCodes.Status401Unauthorized => ErrorCodes.Unauthorized,
                StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
                StatusCodes.Status404NotFound => ErrorCodes.NotFound,
                StatusCodes.Status429TooManyRequests => ErrorCodes.RateLimited,
                _ => ErrorCodes.Internal,
            };
        }
    }

    /// <summary>Model binding errors (malformed JSON, unknown field, wrong type) as validation ProblemDetails.</summary>
    public static IActionResult InvalidModelState(ActionContext context)
    {
        var problem = new ValidationProblemDetails(context.ModelState)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Bad Request",
            Detail = "The request body or parameters are invalid.",
        };
        problem.Extensions["code"] = ErrorCodes.ValidationFailed;
        return new BadRequestObjectResult(problem) { ContentTypes = { "application/problem+json" } };
    }
}

/// <summary>
/// Runs FluentValidation validators for every action argument that has one (level 1 of validation).
/// Failures become a 400 ValidationProblemDetails with field errors.
/// </summary>
public sealed class ValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var argument in context.ActionArguments.Values.Where(a => a is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());
            if (services.GetService(validatorType) is not IValidator validator)
                continue;

            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            foreach (var failure in result.Errors)
            {
                var key = JsonNamingPolicyCamel(failure.PropertyName);
                if (!errors.TryGetValue(key, out var list))
                    errors[key] = list = [];

                list.Add(failure.ErrorMessage);
            }
        }

        if (errors.Count > 0)
            throw new ValidationFailedException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));

        await next();
    }

    private static string JsonNamingPolicyCamel(string propertyPath) =>
        string.Join('.', propertyPath.Split('.').Select(p => JsonNamingPolicy.CamelCase.ConvertName(p)));
}
