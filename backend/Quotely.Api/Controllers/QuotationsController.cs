using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/quotations")]
public class QuotationsController : ControllerBase
{
    private readonly IQuotationService _quotations;
    private readonly IPublicQuotationService _publicQuotations;
    private readonly IPdfService _pdf;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public QuotationsController(
        IQuotationService quotations,
        IPublicQuotationService publicQuotations,
        IPdfService pdf,
        AppDbContext db,
        ICurrentUser currentUser)
    {
        _quotations = quotations;
        _publicQuotations = publicQuotations;
        _pdf = pdf;
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<QuotationListItemDto>>> List(
        [FromQuery] string? search, [FromQuery] string? status,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
        => Ok(await _quotations.ListAsync(_currentUser.Id, search, status, page, pageSize, ct));

    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> Stats(CancellationToken ct)
        => Ok(await _quotations.GetDashboardAsync(_currentUser.Id, ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QuotationDto>> Get(Guid id, CancellationToken ct)
        => Ok(await _quotations.GetAsync(_currentUser.Id, id, ct));

    [HttpPost]
    public async Task<ActionResult<QuotationDto>> Create(SaveQuotationRequest request, CancellationToken ct)
    {
        var created = await _quotations.CreateAsync(_currentUser.Id, request, ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<QuotationDto>> Update(Guid id, SaveQuotationRequest request, CancellationToken ct)
        => Ok(await _quotations.UpdateAsync(_currentUser.Id, id, request, ct));

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        await _quotations.DeleteAsync(_currentUser.Id, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Creates the customer-facing share link for one of the caller's quotations and returns the
    /// full URL. The raw token is only ever present in this response — the database keeps a hash —
    /// so calling this again issues a fresh link and retires the previous one.
    /// </summary>
    [HttpPost("{id:guid}/public-link")]
    [ProducesResponseType(typeof(PublicQuotationLinkDto), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicQuotationLinkDto>> CreatePublicLink(Guid id, CancellationToken ct)
        => Ok(await _publicQuotations.CreateLinkAsync(_currentUser.Id, id, ct));

    /// <summary>Generates the PDF on the fly. Nothing is stored server-side.</summary>
    [HttpPost("{id:guid}/pdf")]
    [HttpGet("{id:guid}/pdf")]
    public async Task<IActionResult> GeneratePdf(Guid id, CancellationToken ct)
    {
        var userId = _currentUser.Id;
        var quotation = await _quotations.GetEntityForPdfAsync(userId, id, ct);
        var business = await _db.BusinessProfiles.AsNoTracking().FirstOrDefaultAsync(b => b.UserId == userId, ct);

        var pdf = _pdf.Generate(quotation, business);

        Response.Headers.Append("Access-Control-Expose-Headers", "Content-Disposition");
        return File(pdf.Content, "application/pdf", pdf.FileName);
    }
}
