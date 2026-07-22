namespace FantasyWarrior.Jobs.Nhl;

// DTOs for api-web.nhle.com /v1/score/{date} and /v1/gamecenter/{id}/boxscore

public sealed class DailyScoresDto
{
    public List<ScoreGameDto> Games { get; set; } = [];
}

public sealed class ScoreGameDto
{
    public long Id { get; set; }
    public long Season { get; set; }
    public int GameType { get; set; }
    public string GameDate { get; set; } = "";
    /// <summary>FUT, LIVE, OFF, FINAL…; only OFF/FINAL games are synced.</summary>
    public string GameState { get; set; } = "";
    public ScoreTeamDto AwayTeam { get; set; } = new();
    public ScoreTeamDto HomeTeam { get; set; } = new();
    public GameOutcomeDto? GameOutcome { get; set; }

    public sealed class ScoreTeamDto
    {
        public string Abbrev { get; set; } = "";
        public int Score { get; set; }
    }

    public sealed class GameOutcomeDto
    {
        public string LastPeriodType { get; set; } = "";
    }
}

public sealed class BoxscoreDto
{
    public PlayerByGameStatsDto PlayerByGameStats { get; set; } = new();

    public sealed class PlayerByGameStatsDto
    {
        public TeamPlayersDto AwayTeam { get; set; } = new();
        public TeamPlayersDto HomeTeam { get; set; } = new();
    }

    public sealed class TeamPlayersDto
    {
        public List<BoxPlayerDto> Forwards { get; set; } = [];
        public List<BoxPlayerDto> Defense { get; set; } = [];
        public List<BoxPlayerDto> Goalies { get; set; } = [];
    }
}

public sealed class BoxPlayerDto
{
    public long PlayerId { get; set; }
    public NhlPlayerDto.LocalizedName? Name { get; set; }
    public string Position { get; set; } = "";
    public string Toi { get; set; } = "";
    public int Pim { get; set; }

    // skaters
    public int? Goals { get; set; }
    public int? Assists { get; set; }
    public int? Points { get; set; }
    public int? PlusMinus { get; set; }
    public int? Sog { get; set; }
    public int? Hits { get; set; }
    public int? BlockedShots { get; set; }
    public int? PowerPlayGoals { get; set; }

    // goalies
    public int? ShotsAgainst { get; set; }
    public int? Saves { get; set; }
    public int? GoalsAgainst { get; set; }
    public string? Decision { get; set; }
    public bool? Starter { get; set; }
}
