namespace GUNRPG.Api.Dtos;

public sealed class ApiStartInfilResponse
{
    public Guid SessionId { get; init; }
    public ApiOperatorStateDto Operator { get; init; } = null!;
}
