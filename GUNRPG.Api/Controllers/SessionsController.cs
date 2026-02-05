using GUNRPG.Api.Dtos;
using GUNRPG.Api.Mapping;
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
    public ActionResult<ApiCombatSessionDto> Create([FromBody] ApiSessionCreateRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiSessionCreateRequest());
        var dto = _service.CreateSession(appRequest);
        var apiDto = ApiMapping.ToApiDto(dto);
        return CreatedAtAction(nameof(GetState), new { id = apiDto.Id }, apiDto);
    }

    [HttpGet("{id:guid}/state")]
    public ActionResult<ApiCombatSessionDto> GetState(Guid id)
    {
        var result = _service.GetState(id);
        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(ApiMapping.ToApiDto(result.Value!));
    }

    [HttpPost("{id:guid}/intent")]
    public ActionResult<ApiIntentSubmissionResultDto> SubmitIntent(Guid id, [FromBody] ApiSubmitIntentsRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiSubmitIntentsRequest());
        var result = _service.SubmitPlayerIntents(id, appRequest);
        var apiResult = ApiMapping.ToApiDto(result);

        if (!result.Accepted && string.Equals(result.Error, "Session not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(apiResult);
        }

        if (!result.Accepted)
        {
            return BadRequest(apiResult);
        }

        return Ok(apiResult);
    }

    [HttpPost("{id:guid}/advance")]
    public ActionResult<ApiCombatSessionDto> Advance(Guid id)
    {
        var result = _service.Advance(id);
        if (!result.IsSuccess)
        {
            var response = string.Equals(result.ErrorMessage, "Session not found", StringComparison.OrdinalIgnoreCase)
                ? NotFound(new { error = result.ErrorMessage })
                : BadRequest(new { error = result.ErrorMessage });
            return response;
        }

        return Ok(ApiMapping.ToApiDto(result.Value!));
    }

    [HttpPost("{id:guid}/pet")]
    public ActionResult<ApiPetStateDto> ApplyPetAction(Guid id, [FromBody] ApiPetActionRequest? request)
    {
        var appRequest = ApiMapping.ToApplicationRequest(request ?? new ApiPetActionRequest());
        var result = _service.ApplyPetAction(id, appRequest);
        if (!result.IsSuccess)
        {
            return NotFound(new { error = result.ErrorMessage });
        }

        return Ok(ApiMapping.ToApiDto(result.Value!));
    }
}
