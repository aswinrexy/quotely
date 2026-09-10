using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Models;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/business-profile")]
public class BusinessProfileController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public BusinessProfileController(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<BusinessProfileDto>> Get(CancellationToken ct)
    {
        var profile = await _db.BusinessProfiles.AsNoTracking()
            .FirstOrDefaultAsync(b => b.UserId == _currentUser.Id, ct);

        return Ok(profile is null ? new BusinessProfileDto() : Map(profile));
    }

    [HttpPut]
    public async Task<ActionResult<BusinessProfileDto>> Update(UpdateBusinessProfileRequest request, CancellationToken ct)
    {
        var userId = _currentUser.Id;
        var profile = await _db.BusinessProfiles.FirstOrDefaultAsync(b => b.UserId == userId, ct);

        if (profile is null)
        {
            profile = new BusinessProfile { Id = Guid.NewGuid(), UserId = userId };
            _db.BusinessProfiles.Add(profile);
        }

        profile.BusinessName = request.BusinessName.Trim();
        profile.BusinessEmail = request.BusinessEmail;
        profile.Phone = request.Phone;
        profile.AddressLine = request.AddressLine;
        profile.City = request.City;
        profile.State = request.State;
        profile.PostalCode = request.PostalCode;
        profile.Country = request.Country;
        profile.TaxNumber = request.TaxNumber;
        profile.LogoUrl = request.LogoUrl;
        profile.Currency = request.Currency.ToUpperInvariant();

        await _db.SaveChangesAsync(ct);
        return Ok(Map(profile));
    }

    private static BusinessProfileDto Map(BusinessProfile b) => new()
    {
        Id = b.Id,
        BusinessName = b.BusinessName,
        BusinessEmail = b.BusinessEmail,
        Phone = b.Phone,
        AddressLine = b.AddressLine,
        City = b.City,
        State = b.State,
        PostalCode = b.PostalCode,
        Country = b.Country,
        TaxNumber = b.TaxNumber,
        LogoUrl = b.LogoUrl,
        Currency = b.Currency
    };
}
