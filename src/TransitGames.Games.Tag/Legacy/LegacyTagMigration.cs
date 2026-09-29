using System.Text.Json;
using System.Text.Json.Nodes;
using TransitGames.Core.Games;
using TransitGames.Core.Persistence;
using TransitGames.Core.Sessions;
using TransitGames.Games.Tag.Resources;

namespace TransitGames.Games.Tag.Legacy;

/// <summary>
/// Games of the first Tag version (two teams swapping roles every round, targets, points) cannot be continued
/// under the new rules. This migration keeps them in the history, read-only: their result is computed once with the
/// old scoring (minutes survived + target points + final bonus per team) and stored in the new state; unfinished ones
/// are closed at their last save. Area and stations stay, so the history still shows the map.
/// </summary>
public sealed class LegacyTagMigration : IGameSessionMigration
{
    public string Id => "tag.v2-rules";

    public string GameId => TagModule.GameId;

    public GameSessionSnapshot? Migrate(GameSessionSnapshot snapshot)
    {
        if (JsonNode.Parse(snapshot.SettingsJson) is not JsonObject settings || settings.ContainsKey("rulesVersion"))
        {
            return null;
        }

        var finishedAt = snapshot.FinishedAt ?? snapshot.UpdatedAt;
        var result = Describe(settings, snapshot.StateJson, snapshot.LocalRoleId, snapshot.RolesVersion, finishedAt);
        settings["rulesVersion"] = TagSettings.CurrentRulesVersion;
        settings["isLegacy"] = true;
        return snapshot with
        {
            Status = SessionStatus.Finished,
            FinishedAt = finishedAt,
            SettingsJson = settings.ToJsonString(GameJson.Options),
            StateJson = JsonSerializer.Serialize(new TagState { LegacyResult = result }, GameJson.Options),
        };
    }

    /// <summary>The old scoreboard: per team and per round.</summary>
    internal static GameResult Describe(JsonObject settings, string stateJson, string localRoleId, int rolesVersion, DateTimeOffset finishedAt)
    {
        var roundDuration = ReadTimeSpan(settings["roundDuration"]) ?? TimeSpan.FromHours(2);
        var pointsMode = !string.Equals((string?)settings["rewardKind"], "Coins", StringComparison.OrdinalIgnoreCase);
        var finalBonus = (int?)settings["finalBonus"] ?? 30;
        var state = JsonNode.Parse(stateJson) as JsonObject;
        var rounds = (state?["rounds"] as JsonArray ?? []).OfType<JsonObject>().Select(r => ReadRound(r, roundDuration, pointsMode, finalBonus, finishedAt)).ToList();

        // The local team: the role it had in round 1 (roles swapped once per round).
        var localIsFirst = localRoleId == TagRoles.Runner == (rolesVersion % 2 == 0);
        string Team(bool first) => (first ? TagStrings.LegacyTeamFirst : TagStrings.LegacyTeamSecond) + (first == localIsFirst ? " " + TagStrings.LegacyYou : string.Empty);

        var first = rounds.Where(r => r.FirstRuns).Sum(r => r.Score);
        var second = rounds.Where(r => !r.FirstRuns).Sum(r => r.Score);
        var headline = first == second
            ? string.Format(TagStrings.LegacyDrawFormat, first, second)
            : string.Format(TagStrings.LegacyWinnerFormat, Team(first > second), first, second);

        var lines = new List<GameResultLine>
        {
            new(Team(true), string.Format(TagStrings.LegacyScoreFormat, first)),
            new(Team(false), string.Format(TagStrings.LegacyScoreFormat, second)),
        };
        lines.AddRange(rounds.Select((r, i) => new GameResultLine(
            string.Format(TagStrings.LegacyRoundFormat, i + 1, Team(r.FirstRuns)),
            string.Format(TagStrings.LegacyRoundDetailsFormat, TagTexts.Duration(r.Survived), r.Captures, r.Score))));
        lines.Add(new GameResultLine(TagStrings.LegacyNoteTitle, TagStrings.LegacyNote));
        return new GameResult(headline, lines);
    }

    private static LegacyRound ReadRound(JsonObject round, TimeSpan roundDuration, bool pointsMode, int finalBonus, DateTimeOffset finishedAt)
    {
        var started = ReadTime(round["startedAt"]) ?? finishedAt;
        var limit = started + roundDuration;
        var ended = ReadTime(round["endedAt"]) ?? (finishedAt < limit ? finishedAt : limit);
        var survived = ended > started ? ended - started : TimeSpan.Zero;
        var captures = round["captures"] as JsonArray ?? [];
        var targetPoints = pointsMode ? captures.OfType<JsonObject>().Sum(c => (int?)c["reward"] ?? 0) : 0;
        var bonus = string.Equals((string?)round["endReason"], "ReachedFinal", StringComparison.OrdinalIgnoreCase) ? finalBonus : 0;
        var firstRuns = !string.Equals((string?)round["runners"], "Second", StringComparison.OrdinalIgnoreCase);
        return new LegacyRound(firstRuns, survived, captures.Count, (int)survived.TotalMinutes + targetPoints + bonus);
    }

    private static TimeSpan? ReadTimeSpan(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && TimeSpan.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var span)
            ? span
            : null;

    private static DateTimeOffset? ReadTime(JsonNode? node) =>
        node is JsonValue value && value.TryGetValue<string>(out var text) && DateTimeOffset.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var time)
            ? time
            : null;

    private sealed record LegacyRound(bool FirstRuns, TimeSpan Survived, int Captures, int Score);
}
