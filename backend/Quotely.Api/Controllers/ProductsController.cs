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
[Route("api/products")]
public class ProductsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _currentUser;

    public ProductsController(AppDbContext db, ICurrentUser currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<ProductDto>>> List(
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 200);

        var query = _db.Products.AsNoTracking().Where(p => p.UserId == _currentUser.Id);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(p => p.Name.Contains(term) || p.Description!.Contains(term));
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(p => p.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => Map(p))
            .ToListAsync(ct);

        return Ok(new PagedResult<ProductDto>(items, page, pageSize, total));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Get(Guid id, CancellationToken ct)
        => Ok(Map(await FindAsync(id, tracking: false, ct)));

    /// <summary>Gated on the subscription; the rule lives in ISubscriptionEntitlementService.</summary>
    [RequiresEntitlement(Entitlement.CreateProduct)]
    [HttpPost]
    public async Task<ActionResult<ProductDto>> Create(SaveProductRequest request, CancellationToken ct)
    {
        var product = new Product { Id = Guid.NewGuid(), UserId = _currentUser.Id };
        Apply(product, request);

        _db.Products.Add(product);
        await _db.SaveChangesAsync(ct);

        return CreatedAtAction(nameof(Get), new { id = product.Id }, Map(product));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<ProductDto>> Update(Guid id, SaveProductRequest request, CancellationToken ct)
    {
        var product = await FindAsync(id, tracking: true, ct);
        Apply(product, request);
        await _db.SaveChangesAsync(ct);
        return Ok(Map(product));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var product = await FindAsync(id, tracking: true, ct);
        // Quotation items keep their snapshot; the product link is simply cleared.
        _db.Products.Remove(product);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<Product> FindAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var query = _db.Products.Where(p => p.Id == id && p.UserId == _currentUser.Id);
        if (!tracking) query = query.AsNoTracking();
        return await query.FirstOrDefaultAsync(ct) ?? throw ApiException.NotFound("Product");
    }

    private static void Apply(Product product, SaveProductRequest r)
    {
        product.Name = r.Name.Trim();
        product.Description = r.Description;
        product.Unit = string.IsNullOrWhiteSpace(r.Unit) ? "Service" : r.Unit.Trim();
        product.Price = r.Price;
        product.TaxRate = r.TaxRate;
    }

    private static ProductDto Map(Product p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        Unit = p.Unit,
        Price = p.Price,
        TaxRate = p.TaxRate,
        CreatedAt = p.CreatedAt
    };
}
