namespace Quotely.Api.Billing;

/// <summary>
/// Who may see the internal billing overview.
///
/// A configured list of email addresses rather than a role in the database, deliberately. A role
/// has to be granted to somebody, which means a way to grant it, which means an endpoint that
/// escalates privilege — and that is a great deal of attack surface for a page that shows a
/// handful of statuses. A list in the host's configuration can only be changed by someone who
/// already controls the deployment.
///
/// Empty by default: no admin view at all until a deployment names someone.
/// </summary>
public class AdminOptions
{
    public const string SectionName = "Admin";

    public string[] Emails { get; set; } = Array.Empty<string>();

    public bool IsAdmin(string? email) =>
        !string.IsNullOrWhiteSpace(email) &&
        Emails.Any(e => string.Equals(e.Trim(), email, StringComparison.OrdinalIgnoreCase));
}
