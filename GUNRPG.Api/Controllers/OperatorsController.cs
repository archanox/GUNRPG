using GUNRPG.Api.Dtos;
using GUNRPG.Api.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;

namespace GUNRPG.Api.Controllers;

/// <summary>
/// Thin controller that only handles HTTP transport mapping for operator operations.
/// All business logic is delegated to the application service.
/// </summary>
[ApiController]
[Route("operators")]
public class OperatorsController : ControllerBase
{
    private readonly OperatorService _service;

    public OperatorsController(OperatorService service)
    {
        _service = service;
    }

    /// <summary>
    /// Gets all operators.
    /// </summary>
    /// <returns>A list of operator summaries.</returns>
    /// <response code="200">Returns the list of operators.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpGet]
    [ProducesResponseType(typeof(List<ApiOperatorSummaryDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<List<ApiOperatorSummaryDto>>> List()
    {
        var result = await _service.ListOperatorsAsync();
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(result.Value!.Select(ApiMapping.ToApiDto).ToList()),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Creates a new operator.
    /// </summary>
    /// <param name="request">The operator creation request containing the operator's name.</param>
    /// <returns>The newly created operator state.</returns>
    /// <response code="201">Operator created successfully.</response>
    /// <response code="400">Invalid request data.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> Create([FromBody] ApiOperatorCreateRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiOperatorCreateRequest());
        var result = await _service.CreateOperatorAsync(appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => CreatedAtAction(nameof(Get), new { id = result.Value!.Id }, ApiMapping.ToApiDto(result.Value)),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Gets a specific operator by ID.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <returns>The operator state.</returns>
    /// <response code="200">Returns the operator state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> Get(Guid id)
    {
        var result = await _service.GetOperatorAsync(id);
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Cleans up a completed combat session for the operator.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <returns>Success on cleanup.</returns>
    /// <response code="200">Session cleaned up successfully.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/cleanup")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CleanupCompletedSession(Guid id)
    {
        var result = await _service.CleanupCompletedSessionAsync(id);
        return result.Status switch
        {
            ResultStatus.Success => Ok(),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Starts an infiltration session for the operator.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <returns>The infil session ID and updated operator state.</returns>
    /// <response code="200">Infiltration started successfully.</response>
    /// <response code="400">Operator is in an invalid state to start infiltration.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/infil/start")]
    [ProducesResponseType(typeof(ApiStartInfilResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiStartInfilResponse>> StartInfil(Guid id)
    {
        var result = await _service.StartInfilAsync(id);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(new ApiStartInfilResponse
            {
                SessionId = result.Value!.SessionId,
                Operator = ApiMapping.ToApiDto(result.Value.Operator)
            }),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Processes the outcome of a combat session for the operator.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <param name="request">The combat outcome details including victory/defeat and XP gained.</param>
    /// <returns>The updated operator state.</returns>
    /// <response code="200">Outcome processed successfully.</response>
    /// <response code="400">Invalid request or operator is in an invalid state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/infil/outcome")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> ProcessOutcome(Guid id, [FromBody] ApiProcessOutcomeRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiProcessOutcomeRequest());
        var result = await _service.ProcessCombatOutcomeAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    [HttpPost("offline/sync")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> SyncOfflineMission([FromBody] ApiOfflineMissionEnvelopeDto envelope)
    {
        var result = await _service.SyncOfflineMission(ApiMapping.ToApplicationDto(envelope));
        return result.Status switch
        {
            ResultStatus.Success => Ok(),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Starts a new combat session during an active infiltration.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <returns>The new combat session ID.</returns>
    /// <response code="200">Combat session started successfully.</response>
    /// <response code="400">Operator is not in an active infiltration.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/infil/combat")]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<Guid>> StartCombatSession(Guid id)
    {
        var result = await _service.StartCombatSessionAsync(id);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(result.Value),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Completes the current infiltration successfully (exfil).
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <returns>Success on exfil completion.</returns>
    /// <response code="200">Infiltration completed successfully.</response>
    /// <response code="400">Operator is in an invalid state to complete infiltration.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/infil/complete")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> CompleteInfil(Guid id)
    {
        var result = await _service.CompleteInfilAsync(id);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Fails the current infiltration (timer expired client-side).
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <returns>Success on infil failure processing.</returns>
    /// <response code="200">Infiltration failure processed successfully.</response>
    /// <response code="400">Operator is in an invalid state to fail infiltration.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/infil/fail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult> FailInfil(Guid id)
    {
        var result = await _service.FailInfilAsync(id, "Infil timer expired");
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Changes the operator's weapon loadout.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <param name="request">The loadout change request containing the new weapon name.</param>
    /// <returns>The updated operator state.</returns>
    /// <response code="200">Loadout changed successfully.</response>
    /// <response code="400">Invalid request or operator is in an invalid state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/loadout")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> ChangeLoadout(Guid id, [FromBody] ApiChangeLoadoutRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiChangeLoadoutRequest());
        var result = await _service.ChangeLoadoutAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Treats the operator's wounds, restoring health.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <param name="request">The wound treatment request containing the amount of health to restore.</param>
    /// <returns>The updated operator state.</returns>
    /// <response code="200">Wounds treated successfully.</response>
    /// <response code="400">Invalid request or operator is in an invalid state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/wounds/treat")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> TreatWounds(Guid id, [FromBody] ApiTreatWoundsRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiTreatWoundsRequest());
        var result = await _service.TreatWoundsAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Applies experience points to the operator.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <param name="request">The XP application request containing the amount and reason.</param>
    /// <returns>The updated operator state.</returns>
    /// <response code="200">XP applied successfully.</response>
    /// <response code="400">Invalid request or operator is in an invalid state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/xp")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> ApplyXp(Guid id, [FromBody] ApiApplyXpRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiApplyXpRequest());
        var result = await _service.ApplyXpAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Unlocks a perk for the operator.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <param name="request">The perk unlock request containing the perk name.</param>
    /// <returns>The updated operator state.</returns>
    /// <response code="200">Perk unlocked successfully.</response>
    /// <response code="400">Invalid request or operator is in an invalid state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/perks")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> UnlockPerk(Guid id, [FromBody] ApiUnlockPerkRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiUnlockPerkRequest());
        var result = await _service.UnlockPerkAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    /// <summary>
    /// Applies a pet action for the operator's companion.
    /// </summary>
    /// <param name="id">The operator's unique identifier.</param>
    /// <param name="request">The pet action request containing the action type and parameters.</param>
    /// <returns>The updated operator state.</returns>
    /// <response code="200">Pet action applied successfully.</response>
    /// <response code="400">Invalid request or operator is in an invalid state.</response>
    /// <response code="404">Operator not found.</response>
    /// <response code="500">An unexpected error occurred.</response>
    [HttpPost("{id:guid}/pet")]
    [ProducesResponseType(typeof(ApiOperatorStateDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<ApiOperatorStateDto>> ApplyPetAction(Guid id, [FromBody] ApiPetActionRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiPetActionRequest());
        var result = await _service.ApplyPetActionAsync(id, appRequest);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }
}
