using GUNRPG.Api.Dtos;
using GUNRPG.Api.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Services;
using GUNRPG.Application.Sessions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// Thin controller that only handles HTTP transport mapping.
/// All business logic is delegated to the application service.
/// </summary>
[ApiController]
[Route("sessions")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly CombatSessionService _service;
    private readonly CombatSessionUpdateHub _updateHub;
    private readonly OperatorService _operatorService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(CombatSessionService service, CombatSessionUpdateHub updateHub, OperatorService operatorService, ILogger<SessionsController> logger)
    {
        _service = service;
        _updateHub = updateHub;
        _operatorService = operatorService;
        _logger = logger;
    }

    /// <summary>
    /// Extracts the account ID from the authenticated user's JWT claims.
    /// Returns Guid.Empty if the claim is absent or cannot be parsed.
    /// </summary>
    private Guid GetAccountId()
    {
        var claim = User.FindFirst("account_id")?.Value;
        return Guid.TryParse(claim, out var id) ? id : Guid.Empty;
    }

    /// <summary>
    /// Verifies that the authenticated account owns the session identified by <paramref name="sessionId"/>.
    /// Returns a 401 when the JWT lacks the account_id claim, a 404 when the session is not found,
    /// a 403 when the session belongs to a different account, or null when ownership is confirmed.
    /// </summary>
    private async Task<ActionResult?> VerifySessionOwnershipAsync(Guid sessionId)
    {
        var accountId = GetAccountId();
        if (accountId == Guid.Empty)
            return Unauthorized(new { error = "Token is missing the required account_id claim." });

        var sessionResult = await _service.GetStateAsync(sessionId);
        if (sessionResult.Status == ResultStatus.NotFound)
            return NotFound(new { error = sessionResult.ErrorMessage });
        if (sessionResult.Status != ResultStatus.Success)
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Unexpected error" });

        var operatorId = sessionResult.Value!.OperatorId;
        var ownerAccountId = await _operatorService.GetOperatorAccountIdAsync(operatorId);

        if (!ownerAccountId.HasValue)
        {
            _logger.LogWarning(
                "Session {SessionId} references operator {OperatorId} that has no account association.",
                sessionId, operatorId);
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "You do not have permission to access this session." });
        }

        if (ownerAccountId.Value != accountId)
            return StatusCode(StatusCodes.Status403Forbidden,
                new { error = "You do not have permission to access this session." });

        return null;
    }

    /// <summary>
    /// Creates a new combat session.
    /// </summary>
    /// <param name="request">The session creation request with optional configuration parameters.</param>
    /// <returns>The newly created combat session state.</returns>
    /// <response code="201">Session created successfully.</response>
    /// <response code="400">Invalid request data.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">The OperatorId in the request belongs to a different account.</response>
    /// <response code="409">A session conflict occurred.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> Create([FromBody] ApiSessionCreateRequest? request)
    {
        var req = request ?? new ApiSessionCreateRequest();

        if (req.OperatorId.HasValue && req.OperatorId.Value != Guid.Empty)
        {
            var accountId = GetAccountId();
            if (accountId == Guid.Empty)
                return Unauthorized(new { error = "Token is missing the required account_id claim." });

            var ownerAccountId = await _operatorService.GetOperatorAccountIdAsync(req.OperatorId.Value);
            if (!ownerAccountId.HasValue || ownerAccountId.Value != accountId)
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "You do not have permission to create a session for this operator." });
        }

        var appRequest = ApiMapping.ToApplicationRequest(req);
        var result = await _service.CreateSessionAsync(appRequest);
        return result.Status switch
        {
            ResultStatus.Success => CreatedAtAction(nameof(GetState), new { id = result.Value!.Id }, ApiMapping.ToApiDto(result.Value)),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => Conflict(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    /// <summary>
    /// Gets the current state of a combat session.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <returns>The current combat session state.</returns>
    /// <response code="200">Returns the combat session state.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">The session belongs to a different account.</response>
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpGet("{id:guid}/state")]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> GetState(Guid id)
    {
        var ownershipCheck = await VerifySessionOwnershipAsync(id);
        if (ownershipCheck is not null) return ownershipCheck;

        var result = await _service.GetStateAsync(id);
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    /// <summary>
    /// Submits player intents for the current combat turn.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <param name="request">The player intents including primary action, movement, stance, and cover choices.</param>
    /// <returns>The updated combat session state.</returns>
    /// <response code="200">Intents submitted successfully.</response>
    /// <response code="400">Invalid request or session is in an invalid state.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">The session belongs to a different account.</response>
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/intent")]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> SubmitIntent(Guid id, [FromBody] ApiSubmitIntentsRequest? request)
    {
        var ownershipCheck = await VerifySessionOwnershipAsync(id);
        if (ownershipCheck is not null) return ownershipCheck;

        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiSubmitIntentsRequest());
        var result = await _service.SubmitPlayerIntentsAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    /// <summary>
    /// Advances the combat simulation by one tick.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <param name="request">Optional request body. When OperatorId is provided, it is validated against the session's owning operator.</param>
    /// <returns>The updated combat session state after advancing.</returns>
    /// <response code="200">Combat advanced successfully.</response>
    /// <response code="400">Session is in an invalid state to advance.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">The session belongs to a different account.</response>
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/advance")]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> Advance(Guid id, [FromBody] ApiAdvanceRequest? request = null)
    {
        var ownershipCheck = await VerifySessionOwnershipAsync(id);
        if (ownershipCheck is not null) return ownershipCheck;

        var result = await _service.AdvanceAsync(id, request?.OperatorId);
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    /// <summary>
    /// Applies a pet action within a combat session.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <param name="request">The pet action request containing the action type and parameters.</param>
    /// <returns>The updated pet state.</returns>
    /// <response code="200">Pet action applied successfully.</response>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">The session belongs to a different account.</response>
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/pet")]
    [ProducesResponseType(typeof(ApiPetStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiPetStateDto>> ApplyPetAction(Guid id, [FromBody] ApiPetActionRequest? request)
    {
        var ownershipCheck = await VerifySessionOwnershipAsync(id);
        if (ownershipCheck is not null) return ownershipCheck;

        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiPetActionRequest());
        var result = await _service.ApplyPetActionAsync(id, appRequest);
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    /// <summary>
    /// Combat sessions are retained as audit records and cannot be deleted.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <returns>An error response indicating that deletion is not supported.</returns>
    /// <response code="401">Authentication required.</response>
    /// <response code="403">The session belongs to a different account.</response>
    /// <response code="404">Session not found.</response>
    /// <response code="409">Combat sessions cannot be deleted.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Delete(Guid id)
    {
        var ownershipCheck = await VerifySessionOwnershipAsync(id);
        if (ownershipCheck is not null) return ownershipCheck;

        var result = await _service.DeleteSessionAsync(id);
        return result.Status switch
        {
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => Conflict(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    /// <summary>
    /// Streams real-time combat session state change notifications via Server-Sent Events.
    /// Each event notifies the client to re-fetch the full session state.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <param name="ct">Cancellation token (connection close).</param>
    /// <remarks>
    /// Clients subscribe to this endpoint to receive live updates whenever the combat
    /// session state changes (e.g. when another connected client submits intents or
    /// advances the turn). This enables cross-client real-time synchronisation for the
    /// combat screen without requiring libp2p on the client.
    /// </remarks>
    [HttpGet("{id:guid}/stream")]
    public async Task StreamSessionEvents(Guid id, CancellationToken ct)
    {
        var ownershipCheck = await VerifySessionOwnershipAsync(id);
        if (ownershipCheck is not null)
        {
            var objectResult = (ObjectResult)ownershipCheck;
            Response.StatusCode = objectResult.StatusCode ?? StatusCodes.Status500InternalServerError;
            await Response.WriteAsJsonAsync(objectResult.Value, ct);
            return;
        }

        Response.ContentType = "text/event-stream";
        Response.Headers["Cache-Control"] = "no-cache";
        Response.Headers["X-Accel-Buffering"] = "no";

        await foreach (var sessionId in _updateHub.SubscribeAsync(id, ct))
        {
            var data = System.Text.Json.JsonSerializer.Serialize(new { sessionId });
            await Response.WriteAsync($"data: {data}\n\n", ct);
            await Response.Body.FlushAsync(ct);
        }
    }
}
