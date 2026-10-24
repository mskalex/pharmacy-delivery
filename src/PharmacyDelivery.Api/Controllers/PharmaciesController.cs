using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PharmacyDelivery.Api.Contracts.V1;
using PharmacyDelivery.Services.Abstract;

namespace PharmacyDelivery.Api.Controllers;

/// <summary>Partner pharmacies.</summary>
[Route(RoutePrefix + "pharmacies")]
[AllowAnonymous]
public sealed class PharmaciesController(IPharmacyDirectoryService pharmacies) : ApiControllerBase
{
    /// <summary>Active partner pharmacies with address and location (cached).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<PharmacyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PharmacyDto>>> List(CancellationToken cancellationToken) =>
        (await pharmacies.ListAsync(cancellationToken)).Select(p => p.ToDto()).ToList();
}
