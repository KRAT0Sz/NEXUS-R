namespace KONGOR.MasterServer.Configuration.CodeRedemption;

public class CodeRedemptionConfiguration
{
    public List<RedemptionCode> Codes { get; set; } = [];
}

public class RedemptionCode
{
    public required string Code { get; set; }

    public string Description { get; set; } = string.Empty;

    public RedemptionRewards Rewards { get; set; } = new();

    public int MaxRedemptions { get; set; } = 1;

    public DateTimeOffset ExpiresAt { get; set; }

    public bool Active { get; set; } = true;
}

public class RedemptionRewards
{
    public int GoldCoins { get; set; }

    public int SilverCoins { get; set; }

    public int PlinkoTickets { get; set; }

    public List<int> ProductIdentifiers { get; set; } = [];
}
