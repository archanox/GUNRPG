using GUNRPG.Application.Dtos;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Sessions;
using Microsoft.AspNetCore.Mvc;

namespace GUNRPG.Api.Controllers;

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
    public ActionResult<CombatSessionDto> Create([FromBody] SessionCreateRequest? request)
    {
        var dto = _service.CreateSession(request ?? new SessionCreateRequest());
        return CreatedAtAction(nameof(GetState), new { id = dto.Id }, dto);
    }

    [HttpGet("{id:guid}/state")]
    public ActionResult<CombatSessionDto> GetState(Guid id)
    {
        var state = _service.GetState(id);
        return state == null ? NotFound() : Ok(state);
    }

    [HttpPost("{id:guid}/intent")]
    public ActionResult<IntentSubmissionResultDto> SubmitIntent(Guid id, [FromBody] SubmitIntentsRequest? request)
    {
        var result = _service.SubmitPlayerIntents(id, request ?? new SubmitIntentsRequest());
        if (!result.Accepted && string.Equals(result.Error, "Session not found", StringComparison.OrdinalIgnoreCase))
        {
            return NotFound(result);
        }

        if (!result.Accepted)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("{id:guid}/advance")]
    public ActionResult<CombatSessionDto> Advance(Guid id)
    {
        try
        {
            var state = _service.Advance(id);
            return Ok(state);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    [HttpPost("{id:guid}/pet")]
    public ActionResult<PetStateDto> ApplyPetAction(Guid id, [FromBody] PetActionRequest? request)
    {
        try
        {
            var state = _service.ApplyPetAction(id, request ?? new PetActionRequest());
            return Ok(state);
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }
}
