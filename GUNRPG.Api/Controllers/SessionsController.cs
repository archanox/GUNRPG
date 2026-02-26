using GUNRPG.Api.Dtos;
using GUNRPG.Api.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// Thin controller that only handles HTTP transport mapping.
/// All business logic is delegated to the application service.
/// </summary>
[ApiController]
[Route("sessions")]
public class SessionsController : ControllerBase
{
    private readonly CombatSessionService _service;

    public SessionsController(CombatSessionService service)
    {
        _service = service;
    }

    /// <summary>
    /// Creates a new combat session.
    /// </summary>
    /// <param name="request">The session creation request with optional configuration parameters.</param>
    /// <returns>The newly created combat session state.</returns>
    /// <response code="201">Session created successfully.</response>
    /// <response code="400">Invalid request data.</response>
    /// <response code="409">A session conflict occurred.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> Create([FromBody] ApiSessionCreateRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiSessionCreateRequest());
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
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpGet("{id:guid}/state")]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> GetState(Guid id)
    {
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
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/intent")]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> SubmitIntent(Guid id, [FromBody] ApiSubmitIntentsRequest? request)
    {
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
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/advance")]
    [ProducesResponseType(typeof(ApiCombatSessionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiCombatSessionDto>> Advance(Guid id, [FromBody] ApiAdvanceRequest? request = null)
    {
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
    /// <response code="404">Session not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/pet")]
    [ProducesResponseType(typeof(ApiPetStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiPetStateDto>> ApplyPetAction(Guid id, [FromBody] ApiPetActionRequest? request)
    {
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
    /// Deletes a combat session.
    /// </summary>
    /// <param name="id">The combat session's unique identifier.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">Session deleted successfully.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> Delete(Guid id)
    {
        var result = await _service.DeleteSessionAsync(id);
        return result.Status switch
        {
            ResultStatus.Success => NoContent(),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }
}
