namespace KONGOR.MasterServer.Controllers.Ascension;

using System.Text.Json;
using MERRICK.DatabaseContext.Enumerations;
using MERRICK.DatabaseContext.Persistence;
using StackExchange.Redis;

[ApiController]
[Route(TextConstant.EmptyString)]
public class AscensionController(MerrickContext databaseContext, IDatabase distributedCache, ILogger<AscensionController> logger) : ControllerBase
{
    private MerrickContext DatabaseContext { get; } = databaseContext;
    private IDatabase DistributedCache { get; } = distributedCache;
    private ILogger Logger { get; } = logger;

    /// <summary>
    ///     Routes ascension API requests based on the "r" query parameter.
    ///     This endpoint handles all client.sea.heroesofnewerth.com requests.
    /// </summary>
    /// <remarks>
    ///     The address is hard-coded in the game client, and needs to be patched either in memory or in the executable file.
    ///
    ///     <code>
    ///         c  l  i  e  n  t  .  s  e  a  .  h  e  r  o  e  s  o  f  n  e  w  e  r  t  h  .  c  o  m
    ///         63 6C 69 65 6E 74 2E 73 65 61 2E 68 65 72 6F 65 73 6F 66 6E 65 77 65 72 74 68 2E 63 6F 6D
    ///     </code>
    /// </remarks>
    [HttpGet("/", Name = "Ascension Root"), HttpGet("index.php", Name = "Ascension Index"), HttpPost("/", Name = "Ascension Root Post"), HttpPost("index.php", Name = "Ascension Index Post")]
    public IActionResult RouteAscensionRequest([FromQuery(Name = "r")] string route)
    {
        if (string.IsNullOrEmpty(route))
            throw new NotImplementedException(@"Ascension Controller Query String Parameter ""r"" Is NULL");

        return route switch
        {
            "api/match/checkmatch"                     => CheckMatch(),
            "api/match/changematchstatus"              => ChangeMatchStatus(),
            "api/match/checkuserrole"                  => CheckUserRole(),
            "api/game/matchresult"                     => ReceiveMatchResult(),
            "api/game/matchstats"                      => ReceiveMatchStatistics(),
            "api/MasterServer/RecordSpectateStartTime" => RecordSpectateStartTime(),
            _                                          => throw new NotImplementedException($@"Unsupported Ascension Controller Route ""{route}""")
        };
    }

    /// <summary>
    ///     Checks if a match is a season match before it starts.
    ///     The server uses this information to determine whether statistics should be recorded or not for the match.
    /// </summary>
    /// <remarks>
    ///     Called by the game server before match initialisation.
    ///     Error code 100 indicates success.
    ///     The "comment" array contains account IDs of voice presenters for the match.
    ///     The "referee" array contains account IDs of live referees for the match.
    /// </remarks>
    private IActionResult CheckMatch()
    {
        string matchID = Request.Query["match_id"].ToString();

        if (string.IsNullOrEmpty(matchID))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""match_id""" });

        return Ok(new { error_code = 100, data = new { is_season_match = true, comment = Array.Empty<string>(), referee = Array.Empty<string>() } });
    }

    /// <summary>
    ///     Receives a match status change notification from the game server.
    /// </summary>
    /// <remarks>
    ///     Called by the game server when match status changes.
    ///     Status 2 indicates the match has started, status 3 indicates the match has ended.
    /// </remarks>
    private IActionResult ChangeMatchStatus()
    {
        string? matchID = Request.Query["match_id"].SingleOrDefault();
        string? statusString = Request.Query["status"].SingleOrDefault();

        if (string.IsNullOrEmpty(matchID))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""match_id""" });

        if (string.IsNullOrEmpty(statusString))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""status""" });

        if (int.TryParse(statusString, out int status).Equals(false))
            return BadRequest(new { error_code = 400, message = @"Invalid Parameter ""status""" });

        if (status is < 0 or > 3)
            return BadRequest(new { error_code = 400, message = @"Invalid Parameter ""status""" });

        string cacheKey = $"Ascension:MatchStatus:{matchID}";
        DistributedCache.StringSet(cacheKey, status, TimeSpan.FromHours(24));

        Logger.LogInformation("Match {MatchID} status changed to {Status}", matchID, status);

        return Ok(new { error_code = 100 });
    }

    /// <summary>
    ///     Checks whether an account has a special role (e.g. referee or game master).
    /// </summary>
    /// <remarks>
    ///     Called by the game client during login.
    ///     A role value of "2" grants game master/referee privileges.
    /// </remarks>
    private IActionResult CheckUserRole()
    {
        string? accountIDString = Request.Query["account_id"].SingleOrDefault();

        if (string.IsNullOrEmpty(accountIDString))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""account_id""" });

        if (int.TryParse(accountIDString, out int accountID).Equals(false))
            return BadRequest(new { error_code = 400, message = @"Invalid Parameter ""account_id""" });

        Account? account = DatabaseContext.Accounts.SingleOrDefault(queriedAccount => queriedAccount.ID == accountID);

        if (account is null)
            return NotFound(new { error_code = 404, message = $@"Account With ID ""{accountID}"" Not Found" });

        bool hasSpecialRole = account.Type is AccountType.GameMaster
            or AccountType.MatchModerator
            or AccountType.MatchCaster
            or AccountType.Staff;

        return Ok(new { error_code = 100, role = hasSpecialRole ? "2" : "0" });
    }

    /// <summary>
    ///     Receives match results from the game server, including betting outcomes such as winning team, first blood team, first tower kill team, and first ten-kill team.
    /// </summary>
    /// <remarks>
    ///     Only called for official/season matches where "is_season_match" was <see langword="true"/> in the <see cref="CheckMatch"/> response.
    /// </remarks>
    private IActionResult ReceiveMatchResult()
    {
        string? matchID = Request.Query["match_id"].SingleOrDefault();

        if (string.IsNullOrEmpty(matchID))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""match_id""" });

        int? winningTeam = null;
        int? firstBloodTeam = null;
        int? firstTowerTeam = null;
        int? firstTenKillTeam = null;

        if (Request.HasFormContentType)
        {
            string? winningTeamString = Request.Form["winning_team"].SingleOrDefault();
            string? firstBloodTeamString = Request.Form["first_blood_team"].SingleOrDefault();
            string? firstTowerTeamString = Request.Form["first_tower_team"].SingleOrDefault();
            string? firstTenKillTeamString = Request.Form["first_10kill_team"].SingleOrDefault();

            if (int.TryParse(winningTeamString, out int wt))
                winningTeam = wt;

            if (int.TryParse(firstBloodTeamString, out int fbt))
                firstBloodTeam = fbt;

            if (int.TryParse(firstTowerTeamString, out int ftt))
                firstTowerTeam = ftt;

            if (int.TryParse(firstTenKillTeamString, out int ftkt))
                firstTenKillTeam = ftkt;
        }

        string cacheKey = $"Ascension:MatchResult:{matchID}";
        var matchResult = new
        {
            MatchID = matchID,
            WinningTeam = winningTeam,
            FirstBloodTeam = firstBloodTeam,
            FirstTowerTeam = firstTowerTeam,
            FirstTenKillTeam = firstTenKillTeam,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
        };

        DistributedCache.StringSet(cacheKey, JsonSerializer.Serialize(matchResult), TimeSpan.FromDays(30));

        Logger.LogInformation("Received match result for match {MatchID}: winning_team={WinningTeam}, first_blood_team={FirstBloodTeam}, first_tower_team={FirstTowerTeam}, first_10kill_team={FirstTenKillTeam}",
            matchID, winningTeam, firstBloodTeam, firstTowerTeam, firstTenKillTeam);

        return Ok(new { error_code = 100 });
    }

    /// <summary>
    ///     Receives live match statistics from the game server as a JSON payload in the "data" POST field.
    ///     Used for live spectating and real-time match tracking.
    /// </summary>
    /// <remarks>
    ///     Only called for official/season matches where "is_season_match" was <see langword="true"/> in the <see cref="CheckMatch"/> response.
    ///     The game server sends periodic updates containing combat logs and match state.
    /// </remarks>
    private IActionResult ReceiveMatchStatistics()
    {
        string? matchID = Request.Query["match_id"].SingleOrDefault();

        if (string.IsNullOrEmpty(matchID))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""match_id""" });

        if (Request.HasFormContentType)
        {
            string? dataJson = Request.Form["data"].SingleOrDefault();

            if (string.IsNullOrEmpty(dataJson).Equals(false))
            {
                try
                {
                    string cacheKey = $"Ascension:MatchStats:{matchID}";
                    DistributedCache.StringSet(cacheKey, dataJson, TimeSpan.FromHours(2));

                    Logger.LogDebug("Received match statistics update for match {MatchID}", matchID);
                }
                catch (JsonException jsonException)
                {
                    Logger.LogWarning(jsonException, "Failed to parse match statistics JSON for match {MatchID}", matchID);

                    return BadRequest(new { error_code = 400, message = @"Invalid JSON in ""data"" parameter" });
                }
            }
        }

        return Ok(new { error_code = 100 });
    }

    /// <summary>
    ///     Records the start time of a spectator session for a given match and region.
    /// </summary>
    /// <remarks>
    ///     Called by the replay manager when a replay recording starts.
    /// </remarks>
    private IActionResult RecordSpectateStartTime()
    {
        string? matchID = Request.Query["match_id"].SingleOrDefault();
        string? accountIDString = Request.Query["account_id"].SingleOrDefault();
        string? region = Request.Query["region"].SingleOrDefault();

        if (string.IsNullOrEmpty(matchID))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""match_id""" });

        if (string.IsNullOrEmpty(accountIDString))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""account_id""" });

        if (string.IsNullOrEmpty(region))
            return BadRequest(new { error_code = 400, message = @"Missing Required Parameter ""region""" });

        if (int.TryParse(accountIDString, out int accountID).Equals(false))
            return BadRequest(new { error_code = 400, message = @"Invalid Parameter ""account_id""" });

        string cacheKey = $"Ascension:SpectateStartTime:{region}:{matchID}:{accountID}";
        DateTimeOffset startTime = DateTimeOffset.UtcNow;

        DistributedCache.StringSet(cacheKey, startTime.ToUnixTimeSeconds(), TimeSpan.FromDays(7));

        Logger.LogInformation("Recorded spectate start time for match {MatchID}, account {AccountID}, region {Region} at {StartTime}",
            matchID, accountID, region, startTime);

        return Ok(new { error_code = 100 });
    }
}
