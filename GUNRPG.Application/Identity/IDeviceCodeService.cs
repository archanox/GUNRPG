using GUNRPG.Application.Identity.Dtos;
using GUNRPG.Application.Results;

namespace GUNRPG.Application.Identity;

/// <summary>
/// Manages the Device Code Flow for console clients that cannot open a browser directly.
/// The console displays a short user code; the user completes WebAuthn in their browser;
/// the console polls until authorization is granted.
/// </summary>
public interface IDeviceCodeService
{
    /// <summary>
    /// Issues a new device code and user code pair.
    /// The caller should display the <see cref="DeviceCodeResponse.UserCode"/> and
    /// <see cref="DeviceCodeResponse.VerificationUri"/> to the end user.
    /// </summary>
    Task<DeviceCodeResponse> StartAsync(CancellationToken ct = default);

    /// <summary>
    /// Authorizes a pending device code after the user completes browser authentication.
    /// Called from the browser-side verification URI handler.
    /// </summary>
    Task<ServiceResult> AuthorizeAsync(string userCode, string userId, CancellationToken ct = default);

    /// <summary>
    /// Polls for the status of a device code authorization.
    /// Enforces the minimum poll interval to prevent abuse.
    /// Returns "pending", "authorized" (with tokens), or "expired".
    /// </summary>
    Task<ServiceResult<DevicePollResponse>> PollAsync(string deviceCode, CancellationToken ct = default);
}
