using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace FantasyWarrior.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NhlTeams",
                columns: table => new
                {
                    Abbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    ConferenceName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    DivisionName = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    LogoUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NhlTeams", x => x.Abbrev);
                });

            migrationBuilder.CreateTable(
                name: "Periods",
                columns: table => new
                {
                    PeriodId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LockUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    GameCount = table.Column<int>(type: "int", nullable: false),
                    FinalizedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Periods", x => x.PeriodId);
                });

            migrationBuilder.CreateTable(
                name: "SimulationState",
                columns: table => new
                {
                    SimulationStateId = table.Column<byte>(type: "tinyint", nullable: false),
                    AsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationState", x => x.SimulationStateId);
                    table.CheckConstraint("CK_SimulationState_SingleRow", "[SimulationStateId] = 1");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ExternalAuthId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Games",
                columns: table => new
                {
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    GameType = table.Column<byte>(type: "tinyint", nullable: false),
                    GameDate = table.Column<DateOnly>(type: "date", nullable: false),
                    HomeTeamAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    AwayTeamAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    HomeScore = table.Column<int>(type: "int", nullable: false),
                    AwayScore = table.Column<int>(type: "int", nullable: false),
                    LastPeriodType = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    SyncedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Games", x => x.GameId);
                    table.ForeignKey(
                        name: "FK_Games_NhlTeams_AwayTeamAbbrev",
                        column: x => x.AwayTeamAbbrev,
                        principalTable: "NhlTeams",
                        principalColumn: "Abbrev");
                    table.ForeignKey(
                        name: "FK_Games_NhlTeams_HomeTeamAbbrev",
                        column: x => x.HomeTeamAbbrev,
                        principalTable: "NhlTeams",
                        principalColumn: "Abbrev");
                });

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Position = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: false),
                    PositionGroup = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: false, computedColumnSql: "CASE WHEN [Position] = 'D' THEN 'D' WHEN [Position] = 'G' THEN 'G' ELSE 'F' END", stored: true),
                    TeamAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    Status = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    SweaterNumber = table.Column<int>(type: "int", nullable: true),
                    ShootsCatches = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: true),
                    BirthDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BirthCountry = table.Column<string>(type: "varchar(3)", unicode: false, maxLength: 3, nullable: true),
                    HeightCm = table.Column<int>(type: "int", nullable: true),
                    WeightKg = table.Column<int>(type: "int", nullable: true),
                    HeadshotUrl = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    DraftYear = table.Column<int>(type: "int", nullable: true),
                    DraftRound = table.Column<int>(type: "int", nullable: true),
                    DraftOverall = table.Column<int>(type: "int", nullable: true),
                    DraftTeamAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    DraftChecked = table.Column<bool>(type: "bit", nullable: false),
                    LastSyncedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.PlayerId);
                    table.ForeignKey(
                        name: "FK_Players_NhlTeams_TeamAbbrev",
                        column: x => x.TeamAbbrev,
                        principalTable: "NhlTeams",
                        principalColumn: "Abbrev");
                });

            migrationBuilder.CreateTable(
                name: "Leagues",
                columns: table => new
                {
                    LeagueId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    JoinCode = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    CommissionerUserId = table.Column<int>(type: "int", nullable: false),
                    CapAmount = table.Column<long>(type: "bigint", nullable: true),
                    RosterMin = table.Column<int>(type: "int", nullable: true),
                    RosterMax = table.Column<int>(type: "int", nullable: true),
                    ActiveForwards = table.Column<int>(type: "int", nullable: false),
                    ActiveDefense = table.Column<int>(type: "int", nullable: false),
                    ActiveGoalies = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Leagues", x => x.LeagueId);
                    table.ForeignKey(
                        name: "FK_Leagues_Users_CommissionerUserId",
                        column: x => x.CommissionerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "NewsItems",
                columns: table => new
                {
                    NewsItemId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ExternalKey = table.Column<string>(type: "varchar(200)", unicode: false, maxLength: 200, nullable: false),
                    Headline = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PlayerId = table.Column<long>(type: "bigint", nullable: true),
                    PlayerName = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    PublishedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FetchedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NewsItems", x => x.NewsItemId);
                    table.ForeignKey(
                        name: "FK_NewsItems_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlayerContracts",
                columns: table => new
                {
                    PlayerContractId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    CapHit = table.Column<long>(type: "bigint", nullable: false),
                    Aav = table.Column<long>(type: "bigint", nullable: true),
                    TotalValue = table.Column<long>(type: "bigint", nullable: true),
                    YearsRemaining = table.Column<int>(type: "int", nullable: true),
                    ClauseType = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: true),
                    Source = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    ImportedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerContracts", x => x.PlayerContractId);
                    table.ForeignKey(
                        name: "FK_PlayerContracts_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlayerGameStats",
                columns: table => new
                {
                    GameId = table.Column<long>(type: "bigint", nullable: false),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    GameDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Season = table.Column<string>(type: "char(8)", unicode: false, fixedLength: true, maxLength: 8, nullable: false),
                    GameType = table.Column<byte>(type: "tinyint", nullable: false),
                    TeamAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    OpponentAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: false),
                    Position = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: false),
                    IsGoalie = table.Column<bool>(type: "bit", nullable: false),
                    IsHome = table.Column<bool>(type: "bit", nullable: false),
                    Toi = table.Column<string>(type: "varchar(8)", unicode: false, maxLength: 8, nullable: true),
                    Pim = table.Column<int>(type: "int", nullable: false),
                    Goals = table.Column<int>(type: "int", nullable: true),
                    Assists = table.Column<int>(type: "int", nullable: true),
                    Points = table.Column<int>(type: "int", nullable: true),
                    PlusMinus = table.Column<int>(type: "int", nullable: true),
                    Shots = table.Column<int>(type: "int", nullable: true),
                    Hits = table.Column<int>(type: "int", nullable: true),
                    BlockedShots = table.Column<int>(type: "int", nullable: true),
                    PowerPlayGoals = table.Column<int>(type: "int", nullable: true),
                    ShotsAgainst = table.Column<int>(type: "int", nullable: true),
                    Saves = table.Column<int>(type: "int", nullable: true),
                    GoalsAgainst = table.Column<int>(type: "int", nullable: true),
                    Decision = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: true),
                    Starter = table.Column<bool>(type: "bit", nullable: true),
                    Shutout = table.Column<bool>(type: "bit", nullable: true),
                    OtLoss = table.Column<bool>(type: "bit", nullable: true),
                    SyncedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerGameStats", x => new { x.GameId, x.PlayerId });
                    table.ForeignKey(
                        name: "FK_PlayerGameStats_Games_GameId",
                        column: x => x.GameId,
                        principalTable: "Games",
                        principalColumn: "GameId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlayerGameStats_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId");
                });

            migrationBuilder.CreateTable(
                name: "PlayerInjuries",
                columns: table => new
                {
                    PlayerInjuryId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    InjuryType = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: true),
                    ExpectedReturn = table.Column<DateOnly>(type: "date", nullable: true),
                    Source = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    ReportedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ResolvedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerInjuries", x => x.PlayerInjuryId);
                    table.ForeignKey(
                        name: "FK_PlayerInjuries_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LeagueMembers",
                columns: table => new
                {
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    JoinedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueMembers", x => new { x.LeagueId, x.UserId });
                    table.ForeignKey(
                        name: "FK_LeagueMembers_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LeagueMembers_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "LeagueScoringRules",
                columns: table => new
                {
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    StatKey = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    PointValue = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LeagueScoringRules", x => new { x.LeagueId, x.StatKey });
                    table.ForeignKey(
                        name: "FK_LeagueScoringRules_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    OwnerUserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    FranchiseAbbrev = table.Column<string>(type: "char(3)", unicode: false, fixedLength: true, maxLength: 3, nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.TeamId);
                    table.ForeignKey(
                        name: "FK_Teams_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Teams_NhlTeams_FranchiseAbbrev",
                        column: x => x.FranchiseAbbrev,
                        principalTable: "NhlTeams",
                        principalColumn: "Abbrev");
                    table.ForeignKey(
                        name: "FK_Teams_Users_OwnerUserId",
                        column: x => x.OwnerUserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "DraftPicks",
                columns: table => new
                {
                    DraftPickId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Round = table.Column<int>(type: "int", nullable: false),
                    PickInRound = table.Column<int>(type: "int", nullable: true),
                    OriginalTeamId = table.Column<int>(type: "int", nullable: false),
                    CurrentTeamId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<long>(type: "bigint", nullable: true),
                    UsedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DraftPicks", x => x.DraftPickId);
                    table.ForeignKey(
                        name: "FK_DraftPicks_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DraftPicks_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId");
                    table.ForeignKey(
                        name: "FK_DraftPicks_Teams_CurrentTeamId",
                        column: x => x.CurrentTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_DraftPicks_Teams_OriginalTeamId",
                        column: x => x.OriginalTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                });

            migrationBuilder.CreateTable(
                name: "TeamPeriodLineups",
                columns: table => new
                {
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    SetBy = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    SubmittedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamPeriodLineups", x => new { x.TeamId, x.PeriodId });
                    table.ForeignKey(
                        name: "FK_TeamPeriodLineups_Periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "Periods",
                        principalColumn: "PeriodId");
                    table.ForeignKey(
                        name: "FK_TeamPeriodLineups_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                columns: table => new
                {
                    TradeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    ProposerTeamId = table.Column<int>(type: "int", nullable: false),
                    CounterpartyTeamId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RespondedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ProcessedUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.TradeId);
                    table.ForeignKey(
                        name: "FK_Trades_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Trades_Teams_CounterpartyTeamId",
                        column: x => x.CounterpartyTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_Trades_Teams_ProposerTeamId",
                        column: x => x.ProposerTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                });

            migrationBuilder.CreateTable(
                name: "RosterSpots",
                columns: table => new
                {
                    RosterSpotId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LeagueId = table.Column<int>(type: "int", nullable: false),
                    TeamId = table.Column<int>(type: "int", nullable: false),
                    PlayerId = table.Column<long>(type: "bigint", nullable: false),
                    PositionGroup = table.Column<string>(type: "char(1)", unicode: false, fixedLength: true, maxLength: 1, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    StartReason = table.Column<byte>(type: "tinyint", nullable: false),
                    StartTradeId = table.Column<int>(type: "int", nullable: true),
                    StartDraftPickId = table.Column<int>(type: "int", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndReason = table.Column<byte>(type: "tinyint", nullable: true),
                    EndTradeId = table.Column<int>(type: "int", nullable: true),
                    OpenedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClosedUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterSpots", x => x.RosterSpotId);
                    table.ForeignKey(
                        name: "FK_RosterSpots_DraftPicks_StartDraftPickId",
                        column: x => x.StartDraftPickId,
                        principalTable: "DraftPicks",
                        principalColumn: "DraftPickId");
                    table.ForeignKey(
                        name: "FK_RosterSpots_Leagues_LeagueId",
                        column: x => x.LeagueId,
                        principalTable: "Leagues",
                        principalColumn: "LeagueId");
                    table.ForeignKey(
                        name: "FK_RosterSpots_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId");
                    table.ForeignKey(
                        name: "FK_RosterSpots_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RosterSpots_Trades_EndTradeId",
                        column: x => x.EndTradeId,
                        principalTable: "Trades",
                        principalColumn: "TradeId");
                    table.ForeignKey(
                        name: "FK_RosterSpots_Trades_StartTradeId",
                        column: x => x.StartTradeId,
                        principalTable: "Trades",
                        principalColumn: "TradeId");
                });

            migrationBuilder.CreateTable(
                name: "TradeAssets",
                columns: table => new
                {
                    TradeAssetId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    FromTeamId = table.Column<int>(type: "int", nullable: false),
                    ToTeamId = table.Column<int>(type: "int", nullable: false),
                    AssetType = table.Column<byte>(type: "tinyint", nullable: false),
                    PlayerId = table.Column<long>(type: "bigint", nullable: true),
                    DraftPickId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeAssets", x => x.TradeAssetId);
                    table.CheckConstraint("CK_TradeAssets_ExactlyOneAsset", "([AssetType] = 0 AND [PlayerId] IS NOT NULL AND [DraftPickId] IS NULL) OR ([AssetType] = 1 AND [DraftPickId] IS NOT NULL AND [PlayerId] IS NULL)");
                    table.ForeignKey(
                        name: "FK_TradeAssets_DraftPicks_DraftPickId",
                        column: x => x.DraftPickId,
                        principalTable: "DraftPicks",
                        principalColumn: "DraftPickId");
                    table.ForeignKey(
                        name: "FK_TradeAssets_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "PlayerId");
                    table.ForeignKey(
                        name: "FK_TradeAssets_Teams_FromTeamId",
                        column: x => x.FromTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_TradeAssets_Teams_ToTeamId",
                        column: x => x.ToTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_TradeAssets_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "TradeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TradeVotes",
                columns: table => new
                {
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    FavoredTeamId = table.Column<int>(type: "int", nullable: true),
                    Magnitude = table.Column<byte>(type: "tinyint", nullable: false),
                    VotedUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeVotes", x => new { x.TradeId, x.UserId });
                    table.CheckConstraint("CK_TradeVotes_MagnitudeMatchesVerdict", "([FavoredTeamId] IS NULL AND [Magnitude] = 0) OR ([FavoredTeamId] IS NOT NULL AND [Magnitude] IN (1, 2))");
                    table.ForeignKey(
                        name: "FK_TradeVotes_Teams_FavoredTeamId",
                        column: x => x.FavoredTeamId,
                        principalTable: "Teams",
                        principalColumn: "TeamId");
                    table.ForeignKey(
                        name: "FK_TradeVotes_Trades_TradeId",
                        column: x => x.TradeId,
                        principalTable: "Trades",
                        principalColumn: "TradeId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TradeVotes_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId");
                });

            migrationBuilder.CreateTable(
                name: "RosterAssignments",
                columns: table => new
                {
                    RosterAssignmentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RosterSpotId = table.Column<int>(type: "int", nullable: false),
                    PeriodId = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    EffectiveFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    EffectiveTo = table.Column<DateOnly>(type: "date", nullable: false),
                    GamesPlayed = table.Column<int>(type: "int", nullable: false),
                    Goals = table.Column<int>(type: "int", nullable: false),
                    Assists = table.Column<int>(type: "int", nullable: false),
                    PlusMinus = table.Column<int>(type: "int", nullable: false),
                    Pim = table.Column<int>(type: "int", nullable: false),
                    Shots = table.Column<int>(type: "int", nullable: false),
                    Hits = table.Column<int>(type: "int", nullable: false),
                    BlockedShots = table.Column<int>(type: "int", nullable: false),
                    Wins = table.Column<int>(type: "int", nullable: false),
                    OtLosses = table.Column<int>(type: "int", nullable: false),
                    Shutouts = table.Column<int>(type: "int", nullable: false),
                    GoalsAgainst = table.Column<int>(type: "int", nullable: false),
                    Saves = table.Column<int>(type: "int", nullable: false),
                    ShotsAgainst = table.Column<int>(type: "int", nullable: false),
                    FantasyPoints = table.Column<double>(type: "float", nullable: false),
                    IsFinalized = table.Column<bool>(type: "bit", nullable: false),
                    ScoredUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RosterAssignments", x => x.RosterAssignmentId);
                    table.ForeignKey(
                        name: "FK_RosterAssignments_Periods_PeriodId",
                        column: x => x.PeriodId,
                        principalTable: "Periods",
                        principalColumn: "PeriodId");
                    table.ForeignKey(
                        name: "FK_RosterAssignments_RosterSpots_RosterSpotId",
                        column: x => x.RosterSpotId,
                        principalTable: "RosterSpots",
                        principalColumn: "RosterSpotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "NhlTeams",
                columns: new[] { "Abbrev", "ConferenceName", "DivisionName", "LogoUrl", "Name" },
                values: new object[,]
                {
                    { "ANA", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/ANA_light.svg", "Anaheim Ducks" },
                    { "BOS", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/BOS_light.svg", "Boston Bruins" },
                    { "BUF", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/BUF_light.svg", "Buffalo Sabres" },
                    { "CAR", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/CAR_light.svg", "Carolina Hurricanes" },
                    { "CBJ", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/CBJ_light.svg", "Columbus Blue Jackets" },
                    { "CGY", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/CGY_light.svg", "Calgary Flames" },
                    { "CHI", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/CHI_light.svg", "Chicago Blackhawks" },
                    { "COL", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/COL_light.svg", "Colorado Avalanche" },
                    { "DAL", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/DAL_light.svg", "Dallas Stars" },
                    { "DET", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/DET_light.svg", "Detroit Red Wings" },
                    { "EDM", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/EDM_light.svg", "Edmonton Oilers" },
                    { "FLA", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/FLA_light.svg", "Florida Panthers" },
                    { "LAK", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/LAK_light.svg", "Los Angeles Kings" },
                    { "MIN", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/MIN_light.svg", "Minnesota Wild" },
                    { "MTL", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/MTL_light.svg", "Montreal Canadiens" },
                    { "NJD", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/NJD_light.svg", "New Jersey Devils" },
                    { "NSH", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/NSH_light.svg", "Nashville Predators" },
                    { "NYI", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/NYI_light.svg", "New York Islanders" },
                    { "NYR", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/NYR_light.svg", "New York Rangers" },
                    { "OTT", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/OTT_light.svg", "Ottawa Senators" },
                    { "PHI", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/PHI_light.svg", "Philadelphia Flyers" },
                    { "PIT", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/PIT_light.svg", "Pittsburgh Penguins" },
                    { "SEA", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/SEA_light.svg", "Seattle Kraken" },
                    { "SJS", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/SJS_light.svg", "San Jose Sharks" },
                    { "STL", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/STL_light.svg", "St. Louis Blues" },
                    { "TBL", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/TBL_light.svg", "Tampa Bay Lightning" },
                    { "TOR", "Eastern", "Atlantic", "https://assets.nhle.com/logos/nhl/svg/TOR_light.svg", "Toronto Maple Leafs" },
                    { "UTA", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/UTA_light.svg", "Utah Mammoth" },
                    { "VAN", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/VAN_light.svg", "Vancouver Canucks" },
                    { "VGK", "Western", "Pacific", "https://assets.nhle.com/logos/nhl/svg/VGK_light.svg", "Vegas Golden Knights" },
                    { "WPG", "Western", "Central", "https://assets.nhle.com/logos/nhl/svg/WPG_light.svg", "Winnipeg Jets" },
                    { "WSH", "Eastern", "Metropolitan", "https://assets.nhle.com/logos/nhl/svg/WSH_light.svg", "Washington Capitals" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_DraftPicks_CurrentTeamId_Year_Round",
                table: "DraftPicks",
                columns: new[] { "CurrentTeamId", "Year", "Round" });

            migrationBuilder.CreateIndex(
                name: "IX_DraftPicks_LeagueId_Year_Round_OriginalTeamId",
                table: "DraftPicks",
                columns: new[] { "LeagueId", "Year", "Round", "OriginalTeamId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DraftPicks_OriginalTeamId",
                table: "DraftPicks",
                column: "OriginalTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_DraftPicks_PlayerId",
                table: "DraftPicks",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Games_AwayTeamAbbrev",
                table: "Games",
                column: "AwayTeamAbbrev");

            migrationBuilder.CreateIndex(
                name: "IX_Games_GameDate",
                table: "Games",
                column: "GameDate");

            migrationBuilder.CreateIndex(
                name: "IX_Games_HomeTeamAbbrev",
                table: "Games",
                column: "HomeTeamAbbrev");

            migrationBuilder.CreateIndex(
                name: "IX_Games_Season_GameType_GameDate",
                table: "Games",
                columns: new[] { "Season", "GameType", "GameDate" });

            migrationBuilder.CreateIndex(
                name: "IX_LeagueMembers_UserId",
                table: "LeagueMembers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_CommissionerUserId",
                table: "Leagues",
                column: "CommissionerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Leagues_JoinCode",
                table: "Leagues",
                column: "JoinCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_PlayerId",
                table: "NewsItems",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_PublishedUtc",
                table: "NewsItems",
                column: "PublishedUtc",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_NewsItems_Source_ExternalKey",
                table: "NewsItems",
                columns: new[] { "Source", "ExternalKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Periods_Season_Number",
                table: "Periods",
                columns: new[] { "Season", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Periods_Season_StartDate",
                table: "Periods",
                columns: new[] { "Season", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerContracts_PlayerId_Season",
                table: "PlayerContracts",
                columns: new[] { "PlayerId", "Season" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameStats_GameDate",
                table: "PlayerGameStats",
                column: "GameDate")
                .Annotation("SqlServer:Include", new[] { "PlayerId", "Season", "GameType", "IsGoalie", "Goals", "Assists", "PlusMinus", "Pim", "Shots", "Hits", "BlockedShots", "Decision", "OtLoss", "Shutout", "GoalsAgainst", "Saves", "ShotsAgainst" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerGameStats_PlayerId_GameDate",
                table: "PlayerGameStats",
                columns: new[] { "PlayerId", "GameDate" });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerInjuries_PlayerId",
                table: "PlayerInjuries",
                column: "PlayerId",
                filter: "[ResolvedUtc] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Players_DraftChecked",
                table: "Players",
                column: "DraftChecked",
                filter: "[DraftChecked] = 0");

            migrationBuilder.CreateIndex(
                name: "IX_Players_LastName_FirstName",
                table: "Players",
                columns: new[] { "LastName", "FirstName" });

            migrationBuilder.CreateIndex(
                name: "IX_Players_Status",
                table: "Players",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Players_TeamAbbrev",
                table: "Players",
                column: "TeamAbbrev");

            migrationBuilder.CreateIndex(
                name: "IX_RosterAssignments_PeriodId_IsActive",
                table: "RosterAssignments",
                columns: new[] { "PeriodId", "IsActive" })
                .Annotation("SqlServer:Include", new[] { "RosterSpotId", "FantasyPoints", "GamesPlayed" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterAssignments_RosterSpotId_PeriodId",
                table: "RosterAssignments",
                columns: new[] { "RosterSpotId", "PeriodId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_EndTradeId",
                table: "RosterSpots",
                column: "EndTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_LeagueId_StartDate_EndDate",
                table: "RosterSpots",
                columns: new[] { "LeagueId", "StartDate", "EndDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_PlayerId",
                table: "RosterSpots",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_StartDraftPickId",
                table: "RosterSpots",
                column: "StartDraftPickId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_StartTradeId",
                table: "RosterSpots",
                column: "StartTradeId");

            migrationBuilder.CreateIndex(
                name: "IX_RosterSpots_TeamId",
                table: "RosterSpots",
                column: "TeamId",
                filter: "[EndDate] IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_RosterSpots_OneOpenSpotPerPlayerPerLeague",
                table: "RosterSpots",
                columns: new[] { "LeagueId", "PlayerId" },
                unique: true,
                filter: "[EndDate] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TeamPeriodLineups_PeriodId",
                table: "TeamPeriodLineups",
                column: "PeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_FranchiseAbbrev",
                table: "Teams",
                column: "FranchiseAbbrev");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_LeagueId_OwnerUserId",
                table: "Teams",
                columns: new[] { "LeagueId", "OwnerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Teams_OwnerUserId",
                table: "Teams",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeAssets_DraftPickId",
                table: "TradeAssets",
                column: "DraftPickId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeAssets_FromTeamId",
                table: "TradeAssets",
                column: "FromTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeAssets_PlayerId",
                table: "TradeAssets",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeAssets_ToTeamId",
                table: "TradeAssets",
                column: "ToTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeAssets_TradeId",
                table: "TradeAssets",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_CounterpartyTeamId",
                table: "Trades",
                column: "CounterpartyTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_LeagueId_CreatedUtc",
                table: "Trades",
                columns: new[] { "LeagueId", "CreatedUtc" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Trades_ProposerTeamId",
                table: "Trades",
                column: "ProposerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_Status",
                table: "Trades",
                column: "Status",
                filter: "[Status] = 3");

            migrationBuilder.CreateIndex(
                name: "IX_TradeVotes_FavoredTeamId",
                table: "TradeVotes",
                column: "FavoredTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeVotes_UserId",
                table: "TradeVotes",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_ExternalAuthId",
                table: "Users",
                column: "ExternalAuthId",
                unique: true,
                filter: "[ExternalAuthId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LeagueMembers");

            migrationBuilder.DropTable(
                name: "LeagueScoringRules");

            migrationBuilder.DropTable(
                name: "NewsItems");

            migrationBuilder.DropTable(
                name: "PlayerContracts");

            migrationBuilder.DropTable(
                name: "PlayerGameStats");

            migrationBuilder.DropTable(
                name: "PlayerInjuries");

            migrationBuilder.DropTable(
                name: "RosterAssignments");

            migrationBuilder.DropTable(
                name: "SimulationState");

            migrationBuilder.DropTable(
                name: "TeamPeriodLineups");

            migrationBuilder.DropTable(
                name: "TradeAssets");

            migrationBuilder.DropTable(
                name: "TradeVotes");

            migrationBuilder.DropTable(
                name: "Games");

            migrationBuilder.DropTable(
                name: "RosterSpots");

            migrationBuilder.DropTable(
                name: "Periods");

            migrationBuilder.DropTable(
                name: "DraftPicks");

            migrationBuilder.DropTable(
                name: "Trades");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "Leagues");

            migrationBuilder.DropTable(
                name: "NhlTeams");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
