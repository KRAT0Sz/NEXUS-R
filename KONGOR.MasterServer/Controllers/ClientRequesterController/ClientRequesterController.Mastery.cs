namespace KONGOR.MasterServer.Controllers.ClientRequesterController;

public partial class ClientRequesterController
{
    /// <summary>
    ///     Retrieves account mastery information for the authenticated player.
    ///     Returns hero mastery experience data used by the client to display mastery progress.
    /// </summary>
    private async Task<IActionResult> GetAccountMastery()
    {
        string? cookie = Request.Form["cookie"];

        if (string.IsNullOrWhiteSpace(cookie))
            return Unauthorized(@"Missing Value For Form Parameter ""cookie""");

        string? accountName = await DistributedCache.GetAccountNameForSessionCookie(cookie);

        if (accountName is null)
            return Unauthorized($@"No Session Found For Cookie ""{cookie}""");

        Account? account = await MerrickContext.Accounts
            .SingleOrDefaultAsync(account => account.Name.Equals(accountName));

        if (account is null)
            return NotFound($@"Account With Name ""{accountName}"" Could Not Be Found");

        List<HeroMastery> heroMasteries = await MerrickContext.HeroMasteries
            .Where(mastery => mastery.AccountID == account.ID)
            .ToListAsync();

        List<HeroMasteryInfo> masteryInfo = heroMasteries.Select(mastery => new HeroMasteryInfo
        {
            HeroName = mastery.HeroIdentifier,
            Experience = mastery.MasteryExperience
        }).ToList();

        Dictionary<string, object> response = new ()
        {
            ["error_code"] = 0,
            ["error_msg"] = string.Empty,
            ["account_id"] = account.ID,
            ["mastery_info"] = masteryInfo
        };

        return Ok(PhpSerialization.Serialize(response));
    }
}
