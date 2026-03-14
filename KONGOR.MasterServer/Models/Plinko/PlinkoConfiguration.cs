namespace KONGOR.MasterServer.Models.Plinko;

using KONGOR.MasterServer.Configuration.Store;

public class PlinkoConfiguration
{
    public int TicketCost { get; set; } = 55;
    public int GoldCost { get; set; } = 30;
    public List<PlinkoTier> Tiers { get; set; } = [];
    public List<PlinkoExchangeItem> ExchangeItems { get; set; } = [];
}

public class PlinkoTier
{
    public string Name { get; set; } = string.Empty;
    public double Probability { get; set; }
    public bool IsTicketTier { get; set; }
    public int TicketAmount { get; set; }
    public List<PlinkoProduct> Products { get; set; } = [];
}

public class PlinkoProduct
{
    public int ID { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PrefixedCode { get; set; } = string.Empty;
    public string LocalPath { get; set; } = string.Empty;
    public StoreItemType StoreItemType { get; set; }
}

public class PlinkoExchangeItem
{
    public int ProductID { get; set; }
    public int TicketCost { get; set; }
}
