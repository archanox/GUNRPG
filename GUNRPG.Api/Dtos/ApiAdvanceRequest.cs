namespace GUNRPG.Api.Dtos;

/// <summary>
/// API request for advancing a combat session by one tick.
/// </summary>
public sealed class ApiAdvanceRequest
{
    /// <summary>
    /// Optional caller operator ID. When provided, validated against the session's owning operator.
    /// </summary>
    public Guid? OperatorId { get; init; }
}
