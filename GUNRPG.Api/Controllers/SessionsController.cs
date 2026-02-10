using GUNRPG.Api.Dtos;
using GUNRPG.Api.Mapping;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
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

    [HttpPost]
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

    [HttpGet("{id:guid}/state")]
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

    [HttpPost("{id:guid}/intent")]
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

    [HttpPost("{id:guid}/advance")]
    public async Task<ActionResult<ApiCombatSessionDto>> Advance(Guid id)
    {
        var result = await _service.AdvanceAsync(id);
        return result.Status switch
        {
            ResultStatus.Success => Ok(ApiMapping.ToApiDto(result.Value!)),
            ResultStatus.NotFound => NotFound(new { error = result.ErrorMessage }),
            ResultStatus.InvalidState => BadRequest(new { error = result.ErrorMessage }),
            ResultStatus.ValidationError => BadRequest(new { error = result.ErrorMessage }),
            _ => StatusCode(500, new { error = "Unexpected error" })
        };
    }

    [HttpPost("{id:guid}/pet")]
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
}
