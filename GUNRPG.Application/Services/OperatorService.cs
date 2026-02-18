using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Mapping;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Application.Sessions;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Services;

/// <summary>
/// Application service that orchestrates operator operations and exposes UI-agnostic endpoints.
/// </summary>
public sealed class OperatorService
{
    private readonly OperatorExfilService _exfilService;
    private readonly CombatSessionService _sessionService;
    private readonly IOperatorEventStore _eventStore;

    public OperatorService(OperatorExfilService exfilService, CombatSessionService sessionService, IOperatorEventStore eventStore)
    {
        _exfilService = exfilService;
        _sessionService = sessionService;
        _eventStore = eventStore;
    }

    public async Task<ServiceResult<OperatorStateDto>> CreateOperatorAsync(OperatorCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ServiceResult<OperatorStateDto>.ValidationError("Operator name cannot be empty");
        }

        var result = await _exfilService.CreateOperatorAsync(request.Name);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(result.Value!);
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> GetOperatorAsync(Guid operatorId)
    {
        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        var operatorDto = ToDto(loadResult.Value!);
        
        // If operator has an active session, load and include it
        if (operatorDto.ActiveSessionId.HasValue)
        {
            var sessionResult = await _sessionService.GetStateAsync(operatorDto.ActiveSessionId.Value);
            if (sessionResult.Status == ResultStatus.Success)
            {
                var session = sessionResult.Value!;
                
                // If session is completed, auto-process the outcome to clean up the operator state
                if (session.Phase == SessionPhase.Completed)
                {
                    var outcomeResult = await _sessionService.GetCombatOutcomeAsync(operatorDto.ActiveSessionId.Value);
                    if (outcomeResult.Status == ResultStatus.Success)
                    {
                        // Process the outcome automatically (this will clear ActiveSessionId and update operator state)
                        await _exfilService.ProcessCombatOutcomeAsync(outcomeResult.Value!, playerConfirmed: true);
                        
                        // Reload operator to get updated state after outcome processing
                        var reloadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
                        if (reloadResult.IsSuccess)
                        {
                            operatorDto = ToDto(reloadResult.Value!);
                        }
                    }
                }
                else
                {
                    // Session is still active (Planning phase), include it in the response
                    // Create a new DTO with the active session included
                    operatorDto = new OperatorStateDto
                    {
                        Id = operatorDto.Id,
                        Name = operatorDto.Name,
                        TotalXp = operatorDto.TotalXp,
                        CurrentHealth = operatorDto.CurrentHealth,
                        MaxHealth = operatorDto.MaxHealth,
                        EquippedWeaponName = operatorDto.EquippedWeaponName,
                        UnlockedPerks = operatorDto.UnlockedPerks,
                        ExfilStreak = operatorDto.ExfilStreak,
                        IsDead = operatorDto.IsDead,
                        CurrentMode = operatorDto.CurrentMode,
                        InfilStartTime = operatorDto.InfilStartTime,
                        ActiveSessionId = operatorDto.ActiveSessionId,
                        ActiveCombatSession = session,
                        LockedLoadout = operatorDto.LockedLoadout,
                        Pet = operatorDto.Pet
                    };
                }
            }
        }

        return ServiceResult<OperatorStateDto>.Success(operatorDto);
    }

    public async Task<ServiceResult<List<OperatorSummaryDto>>> ListOperatorsAsync()
    {
        var operatorIds = await _eventStore.ListOperatorIdsAsync();
        var summaries = new List<OperatorSummaryDto>();

        foreach (var operatorId in operatorIds)
        {
            var loadResult = await _exfilService.LoadOperatorAsync(operatorId);
            if (loadResult.IsSuccess)
            {
                var aggregate = loadResult.Value!;
                summaries.Add(new OperatorSummaryDto
                {
                    Id = aggregate.Id.Value,
                    Name = aggregate.Name,
                    CurrentMode = aggregate.CurrentMode.ToString(),
                    IsDead = aggregate.IsDead,
                    TotalXp = aggregate.TotalXp,
                    CurrentHealth = aggregate.CurrentHealth,
                    MaxHealth = aggregate.MaxHealth
                });
            }
        }

        return ServiceResult<List<OperatorSummaryDto>>.Success(summaries);
    }

    public async Task<ServiceResult<StartInfilResponse>> StartInfilAsync(Guid operatorId)
    {
        // Start the infil first to get the session ID and transition operator to Infil mode
        var result = await _exfilService.StartInfilAsync(new OperatorId(operatorId));
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<StartInfilResponse>.FromResult(result);
        }

        var sessionId = result.Value!;

        // Load the operator to get session creation parameters
        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            // Operator load failed after infil was started - fail the infil to keep state consistent
            var loadErrorDetails = loadResult.ErrorMessage ?? "Unknown error";
            var failLoadResult = await _exfilService.FailInfilAsync(new OperatorId(operatorId), $"Operator load failed: {loadErrorDetails}");
            if (failLoadResult.Status != ResultStatus.Success)
            {
                var cleanupError = failLoadResult.ErrorMessage ?? "Unknown error while failing infil";
                Console.Error.WriteLine($"Failed to fail infil for operator {operatorId}: {cleanupError}");
            }
            return ServiceResult<StartInfilResponse>.FromResult(loadResult);
        }

        var operatorAggregate = loadResult.Value!;

        // Create the combat session atomically with the infil
        var sessionRequest = new SessionCreateRequest
        {
            Id = sessionId,
            OperatorId = operatorId,
            PlayerName = operatorAggregate.Name
        };

        var sessionResult = await _sessionService.CreateSessionAsync(sessionRequest);
        if (sessionResult.Status != ResultStatus.Success)
        {
            // Session creation failed - we need to fail the infil to keep state consistent
            var errorDetails = sessionResult.ErrorMessage ?? "Unknown error";
            var failInfilResult = await _exfilService.FailInfilAsync(new OperatorId(operatorId), $"Session creation failed: {errorDetails}");
            if (failInfilResult.Status != ResultStatus.Success)
            {
                // Log the failure to roll back infil so operators can be manually recovered
                var cleanupError = failInfilResult.ErrorMessage ?? "Unknown error while failing infil";
                Console.Error.WriteLine($"Failed to fail infil for operator {operatorId}: {cleanupError}");
            }
            return ServiceResult<StartInfilResponse>.FromResult(sessionResult);
        }

        return ServiceResult<StartInfilResponse>.Success(new StartInfilResponse
        {
            SessionId = sessionId,
            Operator = ToDto(operatorAggregate)
        });
    }

    public async Task<ServiceResult<OperatorStateDto>> ProcessCombatOutcomeAsync(Guid operatorId, ProcessOutcomeRequest request)
    {
        // Load the combat session and get the authoritative outcome
        var outcomeResult = await _sessionService.GetCombatOutcomeAsync(request.SessionId);
        if (outcomeResult.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(outcomeResult);
        }

        var outcome = outcomeResult.Value!;

        var result = await _exfilService.ProcessCombatOutcomeAsync(outcome, playerConfirmed: true);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> ChangeLoadoutAsync(Guid operatorId, ChangeLoadoutRequest request)
    {
        var result = await _exfilService.ChangeLoadoutAsync(new OperatorId(operatorId), request.WeaponName);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> TreatWoundsAsync(Guid operatorId, TreatWoundsRequest request)
    {
        var result = await _exfilService.TreatWoundsAsync(new OperatorId(operatorId), request.HealthAmount);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> ApplyXpAsync(Guid operatorId, ApplyXpRequest request)
    {
        var result = await _exfilService.ApplyXpAsync(new OperatorId(operatorId), request.XpAmount, request.Reason);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> UnlockPerkAsync(Guid operatorId, UnlockPerkRequest request)
    {
        var result = await _exfilService.UnlockPerkAsync(new OperatorId(operatorId), request.PerkName);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<OperatorStateDto>> ApplyPetActionAsync(Guid operatorId, PetActionRequest request)
    {
        var result = await _exfilService.ApplyPetActionAsync(new OperatorId(operatorId), request);
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<OperatorStateDto>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<OperatorStateDto>.FromResult(loadResult);
        }

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    private static OperatorStateDto ToDto(OperatorAggregate aggregate)
    {
        return new OperatorStateDto
        {
            Id = aggregate.Id.Value,
            Name = aggregate.Name,
            TotalXp = aggregate.TotalXp,
            CurrentHealth = aggregate.CurrentHealth,
            MaxHealth = aggregate.MaxHealth,
            EquippedWeaponName = aggregate.EquippedWeaponName,
            UnlockedPerks = aggregate.UnlockedPerks.ToList(),
            ExfilStreak = aggregate.ExfilStreak,
            IsDead = aggregate.IsDead,
            CurrentMode = aggregate.CurrentMode,
            InfilStartTime = aggregate.InfilStartTime,
            ActiveSessionId = aggregate.ActiveSessionId,
            LockedLoadout = aggregate.LockedLoadout,
            Pet = aggregate.PetState != null ? SessionMapping.ToDto(aggregate.PetState) : null
        };
    }
}

public sealed class StartInfilResponse
{
    public Guid SessionId { get; init; }
    public OperatorStateDto Operator { get; init; } = null!;
}
