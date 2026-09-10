using System.Security.Claims;

namespace Quotely.Api.Services;

/// <summary>
/// Resolves the tenant id from the validated JWT only. Client-supplied user ids are never used.
/// </summary>
public interface ICurrentUser
{
    Guid Id { get; }
}

public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    public Guid Id
    {
        get
        {
            var raw = _accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? _accessor.HttpContext?.User.FindFirstValue("sub");
            return Guid.TryParse(raw, out var id)
                ? id
                : throw new UnauthorizedAccessException("Missing or invalid user identity.");
        }
    }
}
