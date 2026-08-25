using System.Security.Claims;
using ClearWise.Api.Auth;
using ClearWise.Api.Data;

namespace ClearWise.Api.Middleware;

/// <summary>
/// Puts the signed-in user and their tenant onto the request scope, from where the
/// connection interceptor pushes the tenant into PostgreSQL for row level security.
/// </summary>
/// <remarks>
/// <b>Both values come from the authenticated principal's claims, never from request
/// input.</b> An earlier version read the tenant from an <c>X-Tenant-Id</c> header, which
/// meant any caller could name any tenant and row level security would faithfully serve them
/// that tenant's books. A claim is signed, so the client cannot choose it.
/// <para>
/// An unauthenticated request leaves both empty. RLS then matches nothing and the request
/// sees no data, which is the correct direction to fail — but authorization, not this
/// middleware, is what actually rejects it.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(
        HttpContext context, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        var principal = context.User;

        if (principal?.Identity?.IsAuthenticated == true)
        {
            if (Guid.TryParse(principal.FindFirstValue(ClearWiseClaims.TenantId), out var tenantId))
            {
                tenantContext.SetTenant(tenantId);
            }

            if (Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
                || Guid.TryParse(principal.FindFirstValue("sub"), out userId))
            {
                currentUser.SetUser(userId);
            }
        }

        await next(context);
    }
}
