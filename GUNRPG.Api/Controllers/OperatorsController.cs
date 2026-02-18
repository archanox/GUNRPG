using GUNRPG.Api.Dtos;
using GUNRPG.Api.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Services;
using Microsoft.AspNetCore.Mvc;

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

    [HttpGet]
    public async Task<ActionResult<List<ApiOperatorSummaryDto>>> List()
    {
        var result = await _service.ListOperatorsAsync();
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(result.Value!.Select(ApiMapping.ToApiDto).ToList()),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    [HttpPost]
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

    [HttpGet("{id:guid}")]
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

    [HttpPost("{id:guid}/cleanup")]
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

    [HttpPost("{id:guid}/infil/start")]
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

    [HttpPost("{id:guid}/infil/outcome")]
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

    [HttpPost("{id:guid}/infil/retreat")]
    public async Task<ActionResult<ApiOperatorStateDto>> RetreatFromInfil(Guid id)
    {
        var result = await _service.RetreatFromInfilAsync(id);
        
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = result.ErrorMessage ?? "Unexpected error" })
        };
    }

    [HttpPost("{id:guid}/loadout")]
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

    [HttpPost("{id:guid}/wounds/treat")]
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

    [HttpPost("{id:guid}/xp")]
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

    [HttpPost("{id:guid}/perks")]
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

    [HttpPost("{id:guid}/pet")]
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
