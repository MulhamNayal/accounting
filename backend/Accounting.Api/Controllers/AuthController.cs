using Accounting.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Accounting.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService auth) : ControllerBase
{
    /// <summary>
    /// Exchanges an email and password for an access token.
    /// </summary>
    /// <remarks>
    /// The only anonymous endpoint in the application. The token carries the tenant as a
    /// signed claim, which is what the client cannot forge — everything else reads the
    /// tenant from there.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("sign-in")]
    public async Task<ActionResult<SignInResponse>> SignInAsync(
        [FromBody] SignInRequest request, CancellationToken cancellationToken)
        => Ok(await auth.SignInAsync(request, cancellationToken));

    /// <summary>Who the current token belongs to. Useful for confirming a session is live.</summary>
    [HttpGet("me")]
    public async Task<ActionResult<WhoAmIResponse>> MeAsync(CancellationToken cancellationToken)
        => Ok(await auth.WhoAmIAsync(cancellationToken));
}
