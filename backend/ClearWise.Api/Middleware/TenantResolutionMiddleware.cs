using ClearWise.Api.Data;

namespace ClearWise.Api.Middleware;

/// <summary>
/// Resolves the tenant for the current request and puts it on <see cref="ITenantContext"/>,
/// from where the connection interceptor pushes it into PostgreSQL.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is a development placeholder.</b> Reading the tenant from a request header means
/// any caller can name any tenant, which is obviously unacceptable in production. It exists
/// so the stack can be exercised before authentication is built; the real implementation
/// takes the tenant from an authenticated principal's claims and never from client input.
/// </para>
/// <para>
/// Outside Development there is no header fallback and no default tenant: an unresolved
/// tenant leaves the context empty, RLS matches nothing, and the request sees no data.
/// </para>
/// </remarks>
public sealed class TenantResolutionMiddleware(RequestDelegate next, IWebHostEnvironment environment)
{
    public const string TenantHeader = "X-Tenant-Id";

    public async Task InvokeAsync(
        HttpContext context, ITenantContext tenantContext, ICurrentUser currentUser)
    {
        if (environment.IsDevelopment())
        {
            if (context.Request.Headers.TryGetValue(TenantHeader, out var header)
                && Guid.TryParse(header.ToString(), out var headerTenantId))
            {
                tenantContext.SetTenant(headerTenantId);
            }
            else
            {
                tenantContext.SetTenant(DevDataSeeder.DemoTenantId);
                // Only the demo tenant has a known user. A caller naming some other tenant
                // gets no acting user, so posting fails rather than attributing entries to
                // somebody from a different tenant.
                currentUser.SetUser(DevDataSeeder.DemoUserId);
            }
        }

        await next(context);
    }
}
