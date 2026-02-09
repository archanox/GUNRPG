using GUNRPG.Application.Combat;
using GUNRPG.Application.Dtos;
using GUNRPG.Application.Operators;
using GUNRPG.Application.Requests;
using GUNRPG.Application.Results;
using GUNRPG.Core.Operators;

namespace GUNRPG.Application.Services;

/// <summary>
/// Application service that orchestrates operator operations and exposes UI-agnostic endpoints.
/// </summary>
public sealed class OperatorService
{
    private readonly OperatorExfilService _exfilService;

    public OperatorService(OperatorExfilService exfilService)
    {
        _exfilService = exfilService;
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

        return ServiceResult<OperatorStateDto>.Success(ToDto(loadResult.Value!));
    }

    public async Task<ServiceResult<StartInfilResponse>> StartInfilAsync(Guid operatorId)
    {
        var result = await _exfilService.StartInfilAsync(new OperatorId(operatorId));
        if (result.Status != ResultStatus.Success)
        {
            return ServiceResult<StartInfilResponse>.FromResult(result);
        }

        var loadResult = await _exfilService.LoadOperatorAsync(new OperatorId(operatorId));
        if (!loadResult.IsSuccess)
        {
            return ServiceResult<StartInfilResponse>.FromResult(loadResult);
        }

        return ServiceResult<StartInfilResponse>.Success(new StartInfilResponse
        {
            SessionId = result.Value,
            Operator = ToDto(loadResult.Value!)
        });
    }

    public async Task<ServiceResult<OperatorStateDto>> ProcessCombatOutcomeAsync(Guid operatorId, ProcessOutcomeRequest request)
    {
        var outcome = new CombatOutcome(
            sessionId: request.SessionId,
            operatorId: new OperatorId(operatorId),
            operatorDied: request.OperatorDied,
            xpGained: request.XpGained,
            gearLost: request.GearLost,
            isVictory: request.IsVictory,
            completedAt: DateTimeOffset.UtcNow);

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
            LockedLoadout = aggregate.LockedLoadout
        };
    }
}

public sealed class StartInfilResponse
{
    public Guid SessionId { get; init; }
    public OperatorStateDto Operator { get; init; } = null!;
}
