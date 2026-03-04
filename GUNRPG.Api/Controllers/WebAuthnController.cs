using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using GUNRPG.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// WebAuthn credential registration and authentication endpoints.
///
/// HTTPS requirement: WebAuthn ceremonies require a secure context.
/// All configured origins must be HTTPS (localhost exempted for development).
/// This is validated at startup in <see cref="WebAuthnService"/>.
/// </summary>
[ApiController]
[Route("auth/webauthn")]
public sealed class WebAuthnController : ControllerBase
{
    private readonly IWebAuthnService _webAuthn;
    private readonly ITokenService _tokens;
    private readonly UserManager<ApplicationUser> _userManager;

    public WebAuthnController(
        IWebAuthnService webAuthn,
        ITokenService tokens,
        UserManager<ApplicationUser> userManager)
    {
        _webAuthn = webAuthn;
        _tokens = tokens;
        _userManager = userManager;
    }

    /// <summary>
    /// Begins WebAuthn credential registration for the given username.
    /// Returns a JSON options object for <c>navigator.credentials.create()</c>.
    /// </summary>
    [HttpPost("register/begin")]
    public async Task<IActionResult> BeginRegistration([FromBody] WebAuthnBeginRequest request, CancellationToken ct)
    {
        var result = await _webAuthn.BeginRegistrationAsync(request.Username, ct);
        return result.IsSuccess
            ? Content(result.Value!, "application/json")
            : MapWebAuthnError(result);
    }

    /// <summary>
    /// Completes WebAuthn credential registration and issues JWT tokens.
    /// </summary>
    [HttpPost("register/complete")]
    public async Task<IActionResult> CompleteRegistration(
        [FromBody] WebAuthnRegisterCompleteRequest request, CancellationToken ct)
    {
        var regResult = await _webAuthn.CompleteRegistrationAsync(
            request.Username, request.AttestationResponseJson, ct);
        if (!regResult.IsSuccess) return MapWebAuthnError(regResult);

        return await IssueTokensForUser(regResult.Value!, ct);
    }

    /// <summary>
    /// Begins a WebAuthn authentication assertion.
    /// Returns a JSON options object for <c>navigator.credentials.get()</c>.
    /// </summary>
    [HttpPost("login/begin")]
    public async Task<IActionResult> BeginLogin([FromBody] WebAuthnBeginRequest request, CancellationToken ct)
    {
        var result = await _webAuthn.BeginLoginAsync(request.Username, ct);
        return result.IsSuccess
            ? Content(result.Value!, "application/json")
            : MapWebAuthnError(result);
    }

    /// <summary>
    /// Completes WebAuthn authentication and issues JWT tokens.
    /// </summary>
    [HttpPost("login/complete")]
    public async Task<IActionResult> CompleteLogin(
        [FromBody] WebAuthnLoginCompleteRequest request, CancellationToken ct)
    {
        var loginResult = await _webAuthn.CompleteLoginAsync(
            request.Username, request.AssertionResponseJson, ct);
        if (!loginResult.IsSuccess) return MapWebAuthnError(loginResult);

        return await IssueTokensForUser(loginResult.Value!, ct);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task<IActionResult> IssueTokensForUser(string userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return Problem("User not found after authentication.", statusCode: 500);

        var tokens = await _tokens.IssueTokensAsync(user.Id, user.UserName, user.AccountId, ct);
        return Ok(tokens);
    }

    /// <summary>
    /// Maps WebAuthn service errors to structured HTTP responses with typed error codes.
    /// The service embeds the <see cref="WebAuthnErrorCode"/> as "CODE: message".
    /// </summary>
    private IActionResult MapWebAuthnError(ServiceResultBase result)
    {
        // Parse "CODE: message" format produced by WebAuthnService
        var msg = result.ErrorMessage ?? "Unknown WebAuthn error.";
        WebAuthnErrorCode code = WebAuthnErrorCode.InternalError;
        var description = msg;

        var colonIdx = msg.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx > 0 && Enum.TryParse<WebAuthnErrorCode>(msg[..colonIdx], out var parsed))
        {
            code = parsed;
            description = msg[(colonIdx + 1)..].TrimStart();
        }

        var body = new WebAuthnErrorResponse(code, description);

        return code switch
        {
            WebAuthnErrorCode.UserNotFound => NotFound(body),
            WebAuthnErrorCode.InvalidRequest => BadRequest(body),
            WebAuthnErrorCode.ChallengeMissing => BadRequest(body),
            WebAuthnErrorCode.CredentialNotFound => NotFound(body),
            WebAuthnErrorCode.AttestationFailed => UnprocessableEntity(body),
            WebAuthnErrorCode.AssertionFailed => UnprocessableEntity(body),
            WebAuthnErrorCode.CounterRegression => StatusCode(409, body),
            _ => Problem(description),
        };
    }
}
