using GUNRPG.Application.Identity;
using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// Device Code Flow endpoints for console clients that cannot open a browser directly.
///
/// Flow:
/// 1. Console calls <see cref="StartDeviceCode"/> → shows user_code + verification_uri to user.
/// 2. User opens verification_uri in browser, completes WebAuthn, browser calls <see cref="AuthorizeDevice"/>.
/// 3. Console polls <see cref="PollDeviceCode"/> until status is "authorized" or "expired_token".
///
/// Poll status values align with RFC 8628 §3.5:
///   "authorization_pending" — user has not yet acted; keep polling.
///   "slow_down"             — back off; increase your poll interval.
///   "expired_token"         — code expired; restart the flow.
///   "authorized"            — tokens are in the response body.
/// </summary>
[ApiController]
[Route("auth/device")]
public sealed class DeviceCodeController : ControllerBase
{
    private readonly IDeviceCodeService _deviceCodes;

    public DeviceCodeController(IDeviceCodeService deviceCodes)
    {
        _deviceCodes = deviceCodes;
    }

    /// <summary>
    /// Starts a Device Code flow session.
    /// Returns a <c>device_code</c> for polling and a <c>user_code</c> to display to the user.
    /// </summary>
    [HttpPost("start")]
    [AllowAnonymous]
    public async Task<ActionResult<DeviceCodeResponse>> StartDeviceCode(CancellationToken ct)
    {
        var response = await _deviceCodes.StartAsync(ct);
        return Ok(response);
    }

    /// <summary>
    /// Called from the browser after the user completes WebAuthn authentication.
    /// Binds the user code to the authenticated user so the console can receive tokens on next poll.
    /// Requires a valid JWT Bearer token (the user must already be authenticated).
    /// </summary>
    [HttpPost("authorize")]
    [Authorize]
    public async Task<IActionResult> AuthorizeDevice([FromQuery] string userCode, CancellationToken ct)
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
            return Unauthorized(new { error = "Bearer token missing 'sub' claim." });

        var result = await _deviceCodes.AuthorizeAsync(userCode, userId, ct);
        if (!result.IsSuccess)
        {
            return result.Status == ResultStatus.NotFound
                ? NotFound(new { error = result.ErrorMessage })
                : UnprocessableEntity(new { error = result.ErrorMessage });
        }

        return Ok();
    }

    /// <summary>
    /// Polls for device code authorization status.
    /// HTTP 200 is returned for all recognized states (including <c>slow_down</c> and <c>expired_token</c>)
    /// so the console client can inspect the <c>status</c> field without handling HTTP error codes.
    /// HTTP 404 is returned only if the device_code is completely unknown.
    /// </summary>
    [HttpPost("poll")]
    [AllowAnonymous]
    public async Task<IActionResult> PollDeviceCode([FromBody] DevicePollRequest request, CancellationToken ct)
    {
        var result = await _deviceCodes.PollAsync(request.DeviceCode, ct);

        if (!result.IsSuccess && result.Status == ResultStatus.NotFound)
            return NotFound(new { error = result.ErrorMessage });

        // All recognized poll statuses (authorization_pending, slow_down, expired_token, authorized)
        // return HTTP 200 with the status field — client inspects the field rather than the status code.
        return Ok(result.Value);
    }
}
