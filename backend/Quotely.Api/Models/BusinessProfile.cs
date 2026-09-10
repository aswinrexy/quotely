namespace Quotely.Api.Models;

public class BusinessProfile
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public AppUser? User { get; set; }

    public string BusinessName { get; set; } = string.Empty;
    public string? BusinessEmail { get; set; }
    public string? Phone { get; set; }
    public string? AddressLine { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }
    public string? TaxNumber { get; set; }
    /// <summary>Data URI or absolute URL. Kept as a string so file storage can be swapped in later.</summary>
    public string? LogoUrl { get; set; }
    public string Currency { get; set; } = "INR";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
