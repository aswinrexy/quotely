using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Quotely.Api.Data;
using Quotely.Api.DTOs;
using Quotely.Api.Middleware;
using Quotely.Api.Models;
using Quotely.Api.Services;

namespace Quotely.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly ITokenService _tokens;
    private readonly AppDbContext _db;

    public AuthController(UserManager<AppUser> users, SignInManager<AppUser> signIn, ITokenService tokens, AppDbContext db)
    {
        _users = users;
        _signIn = signIn;
        _tokens = tokens;
        _db = db;
    }

    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var email = request.Email.Trim();

        if (await _users.FindByEmailAsync(email) is not null)
            throw ApiException.Conflict("An account with this email already exists.");

        var user = new AppUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FullName = request.FullName.Trim()
        };

        var result = await _users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            throw ApiException.BadRequest(string.Join(" ", result.Errors.Select(e => e.Description)));

        // Give every account a business profile so the app has something to print on the PDF.
        _db.BusinessProfiles.Add(new BusinessProfile
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            BusinessName = string.IsNullOrWhiteSpace(request.BusinessName)
                ? user.FullName
                : request.BusinessName!.Trim(),
            BusinessEmail = email,
            Currency = "INR"
        });
        await _db.SaveChangesAsync();

        return Ok(BuildResponse(user));
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            throw new ApiException(System.Net.HttpStatusCode.Unauthorized, "Incorrect email or password.");

        var check = await _signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (check.IsLockedOut)
            throw new ApiException(System.Net.HttpStatusCode.TooManyRequests, "Too many attempts. Please try again later.");
        if (!check.Succeeded)
            throw new ApiException(System.Net.HttpStatusCode.Unauthorized, "Incorrect email or password.");

        return Ok(BuildResponse(user));
    }

    private AuthResponse BuildResponse(AppUser user)
    {
        var (token, expires) = _tokens.CreateAccessToken(user);
        return new AuthResponse(token, expires, new UserDto(user.Id, user.Email ?? string.Empty, user.FullName));
    }
}
