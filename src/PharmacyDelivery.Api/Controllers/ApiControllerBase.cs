using Asp.Versioning;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Services.Abstract;
using PharmacyDelivery.Services.Models;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>
/// Base of v1 controllers. Controllers are thin by rule: map DTO → call a service → map result to DTO.
/// No business logic and no database access here (architecture test).
/// </summary>
[ApiController]
[ApiVersion(1.0)]
[Produces("application/json")]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
public abstract class ApiControllerBase : ControllerBase
{
    public const string RoutePrefix = "api/v{version:apiVersion}/";

    protected ICurrentUser CurrentUser => HttpContext.RequestServices.GetRequiredService<ICurrentUser>();

    protected ClientInfo Client =>
        new(Request.Headers.UserAgent.ToString(), HttpContext.Connection.RemoteIpAddress?.ToString());
}
