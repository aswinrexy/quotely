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
    private readonly string? _message;

    /// <summary>
    /// <paramref name="message"/> is a fallback only. The entitlement service explains its own
    /// refusals — "your free plan includes 10 invoices" is more use than anything a controller
    /// could say — and this is used only when it gives no reason.
    /// </summary>
    public RequiresEntitlementAttribute(Entitlement entitlement, string? message = null)
    {
        _entitlement = entitlement;
        _message = message;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        var entitlements = services.GetRequiredService<ISubscriptionEntitlementService>();
        var currentUser = services.GetRequiredService<ICurrentUser>();

        var decision = await entitlements.CheckAsync(
            currentUser.Id, _entitlement, context.HttpContext.RequestAborted);

        if (decision.Allowed)
        {
            await next();
            return;
        }

        // The same envelope every other failure uses, so the browser needs no special case, plus
        // the usage figures where a limit was the reason — enough for the page to say "10 of 10".
        context.Result = new ObjectResult(new
        {
            message = decision.Reason ?? _message ?? "That is part of Quotely Pro.",
            status = 402,
            used = decision.Used,
            limit = decision.Limit,
        })
        {
            StatusCode = StatusCodes.Status402PaymentRequired
        };
    }
}
