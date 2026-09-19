using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Billing;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/customers")]
public class CustomersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerSummaryService _summaries;

    public CustomersController(AppDbContext db, ICurrentUser currentUser, ICustomerSummaryService summaries)
    {
        _db = db;
        _currentUser = currentUser;
        _summaries = summaries;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<CustomerDto>>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = _db.Customers.AsNoTracking().Where(c => c.UserId == _currentUser.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(c =>
                c.Name.Contains(term) ||
                c.CompanyName!.Contains(term) ||
                c.Email!.Contains(term) ||
                c.Phone!.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(c => c.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => Map(c))
            .ToListAsync(ct);

        return Ok(new PagedResult<CustomerDto>(items, page, pageSize, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Get(Guid id, CancellationToken ct)
        => Ok(Map(await FindAsync(id, tracking: false, ct)));

    /// <summary>
    /// What this customer has been invoiced, has paid, and still owes — plus their invoices,
    /// paged in the database rather than filtered in the browser (V2.5).
    /// </summary>
    [HttpGet("{id:guid}/summary")]
    [ProducesResponseType(typeof(CustomerSummaryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CustomerSummaryDto>> Summary(
        Guid id, [FromQuery] int page = 1, [FromQuery] int pageSize = 10, CancellationToken ct = default)
        => Ok(await _summaries.GetAsync(_currentUser.Id, id, page, pageSize, ct));

    /// <summary>Gated on the subscription; the rule lives in ISubscriptionEntitlementService.</summary>
    [RequiresEntitlement(Entitlement.CreateCustomer)]
    [HttpPost]
    public async Task<ActionResult<CustomerDto>> Create(SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = new Customer { Id = Guid.NewGuid(), UserId = _currentUser.Id };
        Apply(customer, request);

        _db.Customers.Add(customer);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = customer.Id }, Map(customer));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<CustomerDto>> Update(Guid id, SaveCustomerRequest request, CancellationToken ct)
    {
        var customer = await FindAsync(id, tracking: true, ct);
        Apply(customer, request);
        await _db.SaveChangesAsync(ct);
        return Ok(Map(customer));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var customer = await FindAsync(id, tracking: true, ct);

        if (await _db.Quotations.AnyAsync(q => q.CustomerId == id, ct))
            throw ApiException.BadRequest("This customer has quotations. Delete those first.");

        _db.Customers.Remove(customer);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Customer> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = _db.Customers.Where(c => c.Id == id && c.UserId == _currentUser.Id);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Customer");
    }

    private static void Apply(Customer customer, SaveCustomerRequest r)
    {
        customer.Name = r.Name.Trim();
        customer.CompanyName = r.CompanyName;
        customer.Email = r.Email;
        customer.Phone = r.Phone;
        customer.AddressLine = r.AddressLine;
        customer.City = r.City;
        customer.State = r.State;
        customer.PostalCode = r.PostalCode;
        customer.Country = r.Country;
        customer.Notes = r.Notes;
    }

    private static CustomerDto Map(Customer c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        CompanyName = c.CompanyName,
        Email = c.Email,
        Phone = c.Phone,
        AddressLine = c.AddressLine,
        City = c.City,
        State = c.State,
        PostalCode = c.PostalCode,
        Country = c.Country,
        Notes = c.Notes,
        CreatedAt = c.CreatedAt
    };
}
