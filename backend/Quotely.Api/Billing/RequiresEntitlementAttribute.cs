using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Quotely.Api.Services;

namespace Quotely.Api.Billing;

/// <summary>
/// Gates an endpoint on the caller's subscription.
///
/// A filter rather than a line inside each action, so the gating rules stay in one readable place
/// — the attribute names the entitlement, and <see cref="ISubscriptionEntitlementService"/> owns
/// what that entitlement means. A reader can see which endpoints are gated by grepping for this
/// attribute, which is not true of a condition buried in a method body.
///
/// The response is 402 Payment Required. Not 403: this is not "you may never do this", it is
/// "this account's subscription has lapsed", and the browser shows a different thing for each.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequiresEntitlementAttribute : Attribute, IAsyncActionFilter
{
    private readonly Entitlement _entitlement;
    private readonly string _message;

    public RequiresEntitlementAttribute(Entitlement entitlement, string message)
    {
        _entitlement = entitlement;
        _message = message;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var entitlements = services.GetRequiredService<ISubscriptionEntitlementService>();
        var currentUser = services.GetRequiredService<ICurrentUser>();

        if (await entitlements.IsAllowedAsync(currentUser.Id, _entitlement, context.HttpContext.RequestAborted))
        {
            await next();
            return;
        }

        // The same envelope every other failure uses, so the browser needs no special case.
        context.Result = new ObjectResult(new { message = _message, status = 402 })
        {
            StatusCode = StatusCodes.Status402PaymentRequired
        };
    }
}
