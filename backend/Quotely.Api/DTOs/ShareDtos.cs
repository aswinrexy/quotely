namespace Quotely.Api.DTOs;

/// <summary>
/// Everything the browser needs to hand a document to a customer, composed server-side while the
/// public URL still exists. One shape for invoices (V2.4) and quotations (V2.6): the client's
/// sharing UI is the same in both cases, and so is the material it opens.
///
/// The message, the figures inside it and both deep links are authored here rather than in React,
/// because the amounts are the business's own financial communication and must come from the
/// records, not from whatever the page happened to be holding.
/// </summary>
public record ShareDto
{
    /// <summary>The public document URL, for the plain "copy link" action.</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>The composed message, shown so the owner can read it before sending.</summary>
    public string Message { get; init; } = string.Empty;

    public string EmailSubject { get; init; } = string.Empty;

    /// <summary>Addressed to the customer when a usable number is on file; unaddressed otherwise.</summary>
    public string WhatsAppUrl { get; init; } = string.Empty;

    /// <summary>Addressed to the customer when an email is on file; unaddressed otherwise.</summary>
    public string MailtoUrl { get; init; } = string.Empty;

    /// <summary>Digits only, or null when no usable number is on file. For display.</summary>
    public string? CustomerPhone { get; init; }

    public string? CustomerEmail { get; init; }
}
