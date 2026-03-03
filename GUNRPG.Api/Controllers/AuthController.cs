using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using GUNRPG.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// Authentication endpoints:
/// - WebAuthn registration and login (for browser clients and YubiKey)
/// - Device Code Flow (for console clients)
/// - JWT refresh token rotation
/// </summary>
[ApiController]
[Route("auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IWebAuthnService _webAuthn;
    private readonly ITokenService _tokens;
    private readonly IDeviceCodeService _deviceCodes;
    private readonly UserManager<ApplicationUser> _userManager;

    public AuthController(
        IWebAuthnService webAuthn,
        ITokenService tokens,
        IDeviceCodeService deviceCodes,
        UserManager<ApplicationUser> userManager)
    {
        _webAuthn = webAuthn;
        _tokens = tokens;
        _deviceCodes = deviceCodes;
        _userManager = userManager;
    }

    // ── WebAuthn Registration ─────────────────────────────────────────────────

    /// <summary>
    /// Begins WebAuthn credential registration for the given username.
    /// Returns JSON options for navigator.credentials.create().
    /// </summary>
    [HttpPost("webauthn/register/begin")]
    [AllowAnonymous]
    public async Task<IActionResult> BeginRegistration([FromBody] WebAuthnBeginRequest request, CancellationToken ct)
    {
        var result = await _webAuthn.BeginRegistrationAsync(request.Username, ct);
        return result.IsSuccess
            ? Content(result.Value!, "application/json")
            : MapError(result);
    }

    /// <summary>
    /// Completes WebAuthn credential registration and issues JWT tokens.
    /// </summary>
    [HttpPost("webauthn/register/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> CompleteRegistration(
        [FromBody] WebAuthnRegisterCompleteRequest request, CancellationToken ct)
    {
        var regResult = await _webAuthn.CompleteRegistrationAsync(request.Username, request.AttestationResponseJson, ct);
        if (!regResult.IsSuccess) return MapError(regResult);

        return await IssueTokensForUser(regResult.Value!, ct);
    }

    // ── WebAuthn Login ────────────────────────────────────────────────────────

    /// <summary>
    /// Begins a WebAuthn authentication assertion.
    /// Returns JSON options for navigator.credentials.get().
    /// </summary>
    [HttpPost("webauthn/login/begin")]
    [AllowAnonymous]
    public async Task<IActionResult> BeginLogin([FromBody] WebAuthnBeginRequest request, CancellationToken ct)
    {
        var result = await _webAuthn.BeginLoginAsync(request.Username, ct);
        return result.IsSuccess
            ? Content(result.Value!, "application/json")
            : MapError(result);
    }

    /// <summary>
    /// Completes WebAuthn authentication and issues JWT tokens.
    /// </summary>
    [HttpPost("webauthn/login/complete")]
    [AllowAnonymous]
    public async Task<IActionResult> CompleteLogin(
        [FromBody] WebAuthnLoginCompleteRequest request, CancellationToken ct)
    {
        var loginResult = await _webAuthn.CompleteLoginAsync(request.Username, request.AssertionResponseJson, ct);
        if (!loginResult.IsSuccess) return MapError(loginResult);

        return await IssueTokensForUser(loginResult.Value!, ct);
    }

    // ── Token Refresh ─────────────────────────────────────────────────────────

    /// <summary>
    /// Exchanges a valid refresh token for a new access + refresh token pair (rotation).
    /// The old refresh token is consumed and cannot be reused.
    /// </summary>
    [HttpPost("token/refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> RefreshToken(
        [FromBody] RefreshTokenRequest request, CancellationToken ct)
    {
        var result = await _tokens.RefreshAsync(request.RefreshToken, ct);
        if (!result.IsSuccess) return MapError(result);
        return Ok(result.Value);
    }

    // ── Device Code Flow ──────────────────────────────────────────────────────

    /// <summary>
    /// Starts a Device Code flow session for a console client.
    /// Returns a device_code (for polling) and a user_code (to show the user).
    /// </summary>
    [HttpPost("device/start")]
    [AllowAnonymous]
    public async Task<IActionResult> StartDeviceCode(CancellationToken ct)
    {
        var response = await _deviceCodes.StartAsync(ct);
        return Ok(response);
    }

    /// <summary>
    /// Called by the web client after the user successfully authenticates with WebAuthn.
    /// Binds the user code to the authenticated user, allowing the console to poll for tokens.
    /// Requires the user to be authenticated (JWT bearer).
    /// </summary>
    [HttpPost("device/authorize")]
    [Authorize]
    public async Task<IActionResult> AuthorizeDevice([FromQuery] string userCode, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized();

        var result = await _deviceCodes.AuthorizeAsync(userCode, userId, ct);
        return result.IsSuccess ? Ok() : MapError(result);
    }

    /// <summary>
    /// Polls for the status of a device code authorization.
    /// Returns "pending", "authorized" (with tokens), or "expired".
    /// Enforces a minimum poll interval to prevent abuse.
    /// </summary>
    [HttpPost("device/poll")]
    [AllowAnonymous]
    public async Task<IActionResult> PollDeviceCode(
        [FromBody] string deviceCode, CancellationToken ct)
    {
        var result = await _deviceCodes.PollAsync(deviceCode, ct);
        if (!result.IsSuccess) return MapError(result);
        return Ok(result.Value);
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

    private IActionResult MapError(ServiceResultBase result) =>
        result.Status switch
        {
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => UnprocessableEntity(new { error = result.ErrorMessage }),
            _ => Problem(result.ErrorMessage),
        };
}
