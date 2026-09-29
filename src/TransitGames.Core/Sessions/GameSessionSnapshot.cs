namespace TransitGames.Core.Sessions;

/// <summary>Serialized state of a session, persisted after every change so a killed app resumes where it was.</summary>
public sealed record GameSessionSnapshot
{
    public required Guid SessionId { get; init; }
    public required string GameId { get; init; }
    public required string LocalRoleId { get; init; }
    public int RolesVersion { get; init; }
    public required SessionStatus Status { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset UpdatedAt { get; init; }
    public required string SettingsJson { get; init; }
    public required string StateJson { get; init; }
}
