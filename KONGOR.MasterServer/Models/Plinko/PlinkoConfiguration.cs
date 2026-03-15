namespace KONGOR.MasterServer.Models.Plinko;

public class PlinkoConfiguration
{
    public required int TicketCost { get; set; }

    public required int GoldCost { get; set; }

    /// <summary>
    /// The fixed layout of tier IDs mapped to UI slot indexes.
    /// The game client expects exactly 6 entries.
    /// </summary>
    public required int[] TierLayout { get; set; }

    public required List<PlinkoPrizeTier> PrizeTiers { get; set; }

    public required List<PlinkoTicketExchangeItem> TicketExchangeItems { get; set; }

    /// <summary>
    /// The allowed batch sizes for multi-drop (e.g., [5, 10] means the player can drop 5 or 10 balls at once).
    /// </summary>
    public int[] AllowedMultiDropCounts { get; set; } = [5, 10];
}

public class PlinkoPrizeTier
{
    /// <summary>
    /// The tier ID (1 = Diamond, 2 = Gold, 3 = Silver, 4 = Bronze, 5 = 60-tickets, 6 = 30-tickets).
    /// </summary>
    public required int TierID { get; set; }

    /// <summary>
    /// The probability weight for this tier (all weights are summed and each is divided by the total).
    /// </summary>
    public required int Weight { get; set; }

    /// <summary>
    /// If TRUE, this tier awards tickets instead of a store item.
    /// </summary>
    public required bool AwardsTickets { get; set; }

    /// <summary>
    /// The number of tickets awarded when this tier is a ticket prize, or when all items in the tier are owned.
    /// </summary>
    public required int TicketAmount { get; set; }

    /// <summary>
    /// The minimum gold cost (inclusive) for store items in this chest tier.
    /// Only relevant when <see cref="AwardsTickets"/> is FALSE.
    /// </summary>
    public int MinimumGoldCost { get; set; } = 0;

    /// <summary>
    /// The maximum gold cost (inclusive) for store items in this chest tier.
    /// Only relevant when <see cref="AwardsTickets"/> is FALSE.
    /// </summary>
    public int MaximumGoldCost { get; set; } = int.MaxValue;

    /// <summary>
    /// If TRUE, only premium items are included in this chest tier.
    /// Only relevant when <see cref="AwardsTickets"/> is FALSE.
    /// </summary>
    public bool PremiumOnly { get; set; } = false;
}

public class PlinkoTicketExchangeItem
{
    /// <summary>
    /// A unique 1-based identifier for the exchange item.
    /// </summary>
    public required int ID { get; set; }

    /// <summary>
    /// The cost in Plinko tickets.
    /// </summary>
    public required int Cost { get; set; }

    /// <summary>
    /// The product ID from <see cref="StoreItemConfiguration"/>.
    /// </summary>
    public required int ProductID { get; set; }
}
