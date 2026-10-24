using System.Diagnostics;
using System.Security.Claims;
using PharmacyDelivery.Common.Security;
using PharmacyDelivery.Repository.Abstract;
using PharmacyDelivery.Repository.Models;

namespace PharmacyDelivery.Api.Infrastructure;

/// <summary>
/// Marks an API action whose database changes are audited (old → new value per property, same transaction).
/// Every state-changing action must carry either this attribute or <see cref="AuditExemptAttribute"/>;
/// an architecture test enforces it.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
public sealed class AuditedAttribute(string action) : Attribute
{
    /// <summary>Stable action name stored in the audit log, e.g. <c>order.cancel</c>.</summary>
    public string Action { get; } = action;
}

/// <summary>Explicitly excludes a state-changing action from the audit log, with a reason.</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class AuditExemptAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}

/// <summary>
/// Opens the audit scope for endpoints marked with <see cref="AuditedAttribute"/>. The EF Core interceptor then
/// records every change saved during the request; this middleware adds a summary line to the application log.
/// </summary>
public sealed class AuditMiddleware(RequestDelegate next, ILogger<AuditMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, IAuditContext auditContext)
    {
        var audited = context.GetEndpoint()?.Metadata.GetMetadata<AuditedAttribute>();
        if (audited is null)
        {
            await next(context);
            return;
        }

        var user = context.User;
        var userId = Guid.TryParse(user.FindFirstValue(ClaimNames.Subject), out var id) ? id : (Guid?)null;
        var role = user.FindFirstValue(ClaimNames.Role) ?? (userId is null ? "anonymous" : null);
        auditContext.Begin(new AuditInfo(
            audited.Action,
            userId,
            role,
            context.Request.Method,
            context.Request.Path.Value ?? string.Empty,
            Activity.Current?.TraceId.ToString()));

        await next(context);

        logger.LogInformation(
            "Audit {AuditAction} by {UserId} ({Role}) -> {StatusCode}",
            audited.Action,
            userId,
            role,
            context.Response.StatusCode);
    }
}
