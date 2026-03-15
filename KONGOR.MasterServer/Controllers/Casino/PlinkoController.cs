using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using KONGOR.MasterServer.Models.Plinko;

namespace KONGOR.MasterServer.Controllers.Casino;

[ApiController]
[Route("master")]
public class PlinkoController(MerrickContext databaseContext, IDatabase distributedCache, ILogger<PlinkoController> logger) : ControllerBase
{
    private MerrickContext MerrickContext { get; } = databaseContext;
    private IDatabase DistributedCache { get; } = distributedCache;
    private ILogger Logger { get; } = logger;

    private static PlinkoConfiguration Configuration => JSONConfiguration.PlinkoConfiguration;
    private static StoreItemsConfiguration StoreItems => JSONConfiguration.StoreItemsConfiguration;

    #region POST /master/casino/ — Open Plinko

    [HttpPost("casino/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Open()
    {
        string? cookie = Request.Form["cookie"];

        if (cookie is null)
            return BadRequest(@"Missing Value For Form Parameter ""cookie""");

        (bool isValid, string? accountName) = await DistributedCache.ValidateAccountSessionCookie(cookie);

        if (isValid.Equals(false) || accountName is null)
        {
            Logger.LogWarning(@"Plinko Request With Invalid Cookie ""{Cookie}"" From ""{IPAddress}""",
                cookie, Request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UNKNOWN");

            return Unauthorized($@"Unrecognized Cookie ""{cookie}""");
        }

        Account account = await MerrickContext.Accounts
            .Include(queriedAccount => queriedAccount.User)
            .SingleAsync(queriedAccount => queriedAccount.Name.Equals(accountName));

        User user = account.User;

        string unixTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();

        List<int> productCounts = [];
        List<string> updateTimestamps = [];

        foreach (int tierID in Configuration.TierLayout)
        {
            PlinkoPrizeTier? tier = Configuration.PrizeTiers.SingleOrDefault(prizeTier => prizeTier.TierID == tierID);

            if (tier is null || tier.AwardsTickets)
            {
                productCounts.Add(0);
                updateTimestamps.Add(unixTimestamp);
                continue;
            }

            List<StoreItem> availableItems = GetAvailableItemsForTier(tier, user.OwnedStoreItems);
            productCounts.Add(availableItems.Count);
            updateTimestamps.Add(unixTimestamp);
        }

        OrderedDictionary response = new()
        {
            ["status_code"] = 1,
            ["tiers"] = Configuration.TierLayout.Select(tierID => tierID.ToString()).ToArray(),
            ["ticket_cost"] = Configuration.TicketCost.ToString(),
            ["gold_cost"] = Configuration.GoldCost.ToString(),
            ["user_gold"] = user.GoldCoins.ToString(),
            ["silver"] = user.SilverCoins.ToString(),
            ["user_tickets"] = user.PlinkoTickets.ToString(),
            ["amount_of_products"] = string.Join(",", productCounts),
            ["last_update_time"] = string.Join(",", updateTimestamps),
            ["multi_drop_counts"] = string.Join(",", Configuration.AllowedMultiDropCounts)
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    #endregion

    #region POST /master/casino/drop/ — Play Plinko

    [HttpPost("casino/drop/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Drop()
    {
        string? cookie = Request.Form["cookie"];

        if (cookie is null)
            return BadRequest(@"Missing Value For Form Parameter ""cookie""");

        string? currency = Request.Form["currency"];

        if (currency is null)
            return BadRequest(@"Missing Value For Form Parameter ""currency""");

        (bool isValid, string? accountName) = await DistributedCache.ValidateAccountSessionCookie(cookie);

        if (isValid.Equals(false) || accountName is null)
        {
            Logger.LogWarning(@"Plinko Drop Request With Invalid Cookie ""{Cookie}"" From ""{IPAddress}""",
                cookie, Request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UNKNOWN");

            return Unauthorized($@"Unrecognized Cookie ""{cookie}""");
        }

        Account account = await MerrickContext.Accounts
            .Include(queriedAccount => queriedAccount.User)
            .SingleAsync(queriedAccount => queriedAccount.Name.Equals(accountName));

        User user = account.User;

        bool payingWithTickets = currency.Equals("tickets", StringComparison.OrdinalIgnoreCase);
        bool payingWithGold = currency.Equals("gold", StringComparison.OrdinalIgnoreCase);

        if (payingWithTickets is false && payingWithGold is false)
            return BadRequest(@$"Invalid Currency Type ""{currency}""");

        if (payingWithTickets && user.PlinkoTickets < Configuration.TicketCost)
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object> { ["status_code"] = 0 }));

        if (payingWithGold && user.GoldCoins < Configuration.GoldCost)
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object> { ["status_code"] = 0 }));

        // Deduct The Cost
        if (payingWithTickets)
            user.PlinkoTickets -= Configuration.TicketCost;
        else
            user.GoldCoins -= Configuration.GoldCost;

        // Roll A Random Tier
        PlinkoPrizeTier rolledTier = RollRandomTier();

        OrderedDictionary response;

        if (rolledTier.AwardsTickets)
        {
            // Award Tickets
            user.PlinkoTickets += rolledTier.TicketAmount;

            response = new OrderedDictionary
            {
                ["user_tickets"] = user.PlinkoTickets.ToString(),
                ["user_gold"] = user.GoldCoins.ToString(),
                ["status_code"] = "1",
                ["random_tier"] = rolledTier.TierID.ToString(),
                ["product_id"] = "-1",
                ["product_name"] = "Ticket",
                ["product_type"] = "Ticket",
                ["product_path"] = "Ticket",
                ["products_exhausted"] = false,
                ["ticket_amount"] = rolledTier.TicketAmount.ToString()
            };
        }
        else
        {
            List<StoreItem> availableItems = GetAvailableItemsForTier(rolledTier, user.OwnedStoreItems);

            if (availableItems.Count == 0)
            {
                // All Items In This Tier Are Owned; Award Fallback Tickets
                user.PlinkoTickets += rolledTier.TicketAmount;

                response = new OrderedDictionary
                {
                    ["user_tickets"] = user.PlinkoTickets.ToString(),
                    ["user_gold"] = user.GoldCoins.ToString(),
                    ["status_code"] = "1",
                    ["random_tier"] = rolledTier.TierID.ToString(),
                    ["product_id"] = "-1",
                    ["product_name"] = "Ticket",
                    ["product_type"] = "Ticket",
                    ["product_path"] = "Ticket",
                    ["products_exhausted"] = true,
                    ["ticket_amount"] = rolledTier.TicketAmount.ToString()
                };
            }
            else
            {
                // Pick A Random Item From The Available Pool
                StoreItem wonItem = availableItems[Random.Shared.Next(availableItems.Count)];

                // Grant The Item To The User
                string ownedItemCode = GetOwnedItemCode(wonItem);
                user.OwnedStoreItems.Add(ownedItemCode);

                // Check If All Items In This Tier Are Now Exhausted
                bool productsExhausted = availableItems.Count == 1;

                response = new OrderedDictionary
                {
                    ["user_tickets"] = user.PlinkoTickets.ToString(),
                    ["user_gold"] = user.GoldCoins.ToString(),
                    ["status_code"] = "1",
                    ["random_tier"] = rolledTier.TierID.ToString(),
                    ["product_id"] = wonItem.ID.ToString(),
                    ["product_name"] = wonItem.Code,
                    ["product_type"] = GetProductTypeDisplayName(wonItem),
                    ["product_path"] = wonItem.Resource,
                    ["products_exhausted"] = productsExhausted,
                    ["ticket_amount"] = "0"
                };
            }
        }

        await MerrickContext.SaveChangesAsync();

        // Send updated owned items immediately so client can use the item without restarting
        if (response.Contains("my_upgrades").Equals(false))
            response["my_upgrades"] = user.OwnedStoreItems;

        return Ok(PhpSerialization.Serialize(response));
    }

    #endregion

    #region POST /master/casino/viewchest/ — View Chest Contents

    [HttpPost("casino/viewchest/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> ViewChest()
    {
        string? cookie = Request.Form["cookie"];

        if (cookie is null)
            return BadRequest(@"Missing Value For Form Parameter ""cookie""");

        string? tierIdString = Request.Form["tier_id"];

        if (tierIdString is null)
            return BadRequest(@"Missing Value For Form Parameter ""tier_id""");

        if (int.TryParse(tierIdString, out int tierID) is false || tierID < 1 || tierID > 4)
            return BadRequest(@$"Invalid Tier ID ""{tierIdString}""");

        string? targetIndexString = Request.Form["target_index"];

        if (targetIndexString is null)
            return BadRequest(@"Missing Value For Form Parameter ""target_index""");

        if (int.TryParse(targetIndexString, out int targetIndex) is false || targetIndex < 1)
            return BadRequest(@$"Invalid Target Index ""{targetIndexString}""");

        (bool isValid, string? accountName) = await DistributedCache.ValidateAccountSessionCookie(cookie);

        if (isValid.Equals(false) || accountName is null)
            return Unauthorized($@"Unrecognized Cookie ""{cookie}""");

        Account account = await MerrickContext.Accounts
            .Include(queriedAccount => queriedAccount.User)
            .SingleAsync(queriedAccount => queriedAccount.Name.Equals(accountName));

        User user = account.User;

        PlinkoPrizeTier? tier = Configuration.PrizeTiers.SingleOrDefault(prizeTier => prizeTier.TierID == tierID);

        if (tier is null || tier.AwardsTickets)
            return BadRequest(@$"Tier ""{tierID}"" Does Not Have Viewable Items");

        List<StoreItem> availableItems = GetAvailableItemsForTier(tier, user.OwnedStoreItems);

        // Paginate: The Client Requests Pages Of Items, Each Page Contains Up To 56 Items
        int pageSize = 56;
        int firstItemIndex = Math.Max(0, targetIndex - 1);
        List<StoreItem> pageItems = availableItems.Skip(firstItemIndex).Take(pageSize).ToList();

        OrderedDictionary response = new()
        {
            ["tier_id"] = tierID,
            ["items_amount"] = availableItems.Count,
            ["first_item_index"] = firstItemIndex + 1,
            ["target_index"] = targetIndex,
            ["product_names"] = string.Join(",", pageItems.Select(item => item.Code)),
            ["product_types"] = string.Join(",", pageItems.Select(item => GetProductTypeAbbreviation(item))),
            ["product_paths"] = string.Join(",", pageItems.Select(item => item.Resource)),
            ["product_ids"] = string.Join(",", pageItems.Select(item => item.ID))
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    #endregion

    #region POST /master/ticketexchange/ — Open Ticket Exchange

    [HttpPost("ticketexchange/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> GetTicketExchange()
    {
        string? cookie = Request.Form["cookie"];

        if (cookie is null)
            return BadRequest(@"Missing Value For Form Parameter ""cookie""");

        (bool isValid, string? accountName) = await DistributedCache.ValidateAccountSessionCookie(cookie);

        if (isValid.Equals(false) || accountName is null)
        {
            Logger.LogWarning(@"Ticket Exchange Request With Invalid Cookie ""{Cookie}"" From ""{IPAddress}""",
                cookie, Request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UNKNOWN");

            return Unauthorized($@"Unrecognized Cookie ""{cookie}""");
        }

        Account account = await MerrickContext.Accounts
            .Include(queriedAccount => queriedAccount.User)
            .SingleAsync(queriedAccount => queriedAccount.Name.Equals(accountName));

        User user = account.User;

        List<Dictionary<string, object>> items = [];

        foreach (PlinkoTicketExchangeItem exchangeItem in Configuration.TicketExchangeItems)
        {
            StoreItem? storeItem = StoreItems.GetByID(exchangeItem.ProductID);

            if (storeItem is null)
                continue;

            items.Add(new Dictionary<string, object>
            {
                ["id"] = exchangeItem.ID,
                ["cost"] = exchangeItem.Cost,
                ["product_id"] = storeItem.ID,
                ["name"] = storeItem.Code,
                ["type"] = GetProductTypeDisplayName(storeItem),
                ["local_path"] = storeItem.Resource
            });
        }

        OrderedDictionary response = new()
        {
            ["status_code"] = 51,
            ["items"] = items,
            ["user_tickets"] = user.PlinkoTickets.ToString()
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    #endregion

    #region POST /master/ticketexchange/purchase/ — Purchase With Tickets

    [HttpPost("ticketexchange/purchase/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> PurchaseWithTickets()
    {
        string? cookie = Request.Form["cookie"];

        if (cookie is null)
            return BadRequest(@"Missing Value For Form Parameter ""cookie""");

        string? productIdString = Request.Form["id"];

        if (productIdString is null)
            return BadRequest(@"Missing Value For Form Parameter ""id""");

        if (int.TryParse(productIdString, out int productId).Equals(false))
            return BadRequest(@"Invalid Value For Form Parameter ""id""");

        (bool isValid, string? accountName) = await DistributedCache.ValidateAccountSessionCookie(cookie);

        if (isValid.Equals(false) || accountName is null)
        {
            Logger.LogWarning(@"Ticket Exchange Purchase Request With Invalid Cookie ""{Cookie}"" From ""{IPAddress}""",
                cookie, Request.HttpContext.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "UNKNOWN");

            return Unauthorized($@"Unrecognized Cookie ""{cookie}""");
        }

        Account account = await MerrickContext.Accounts
            .Include(queriedAccount => queriedAccount.User)
            .SingleAsync(queriedAccount => queriedAccount.Name.Equals(accountName));

        User user = account.User;

        PlinkoTicketExchangeItem? exchangeItem = Configuration.TicketExchangeItems.FirstOrDefault(item => item.ProductID == productId);

        if (exchangeItem is null)
        {
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
            {
                ["status_code"] = 0,
                ["error_message"] = "Invalid product"
            }));
        }

        if (user.PlinkoTickets < exchangeItem.Cost)
        {
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
            {
                ["status_code"] = 0,
                ["error_message"] = "Not enough tickets"
            }));
        }

        StoreItem? storeItem = StoreItems.GetByID(productId);

        if (storeItem is null)
        {
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
            {
                ["status_code"] = 0,
                ["error_message"] = "Product not found"
            }));
        }

        user.PlinkoTickets -= exchangeItem.Cost;

        string ownedItemCode = GetOwnedItemCode(storeItem);
        if (user.OwnedStoreItems.Contains(ownedItemCode).Equals(false))
        {
            user.OwnedStoreItems.Add(ownedItemCode);
        }

        await MerrickContext.SaveChangesAsync();

        OrderedDictionary response = new()
        {
            ["status_code"] = 51,
            ["tickets_remaining"] = user.PlinkoTickets.ToString(),
            ["grabBag"] = false,
            ["my_upgrades"] = user.OwnedStoreItems
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    #endregion

    #region Helper Methods

    private static List<StoreItem> GetAvailableItemsForTier(PlinkoPrizeTier tier, List<string> ownedStoreItems)
    {
        return StoreItems.StoreItems
            .Where(item => item.IsEnabled && item.IsBundle is false && item.Purchasable)
            .Where(item => item.GoldCost >= tier.MinimumGoldCost && item.GoldCost <= tier.MaximumGoldCost)
            .Where(item => tier.PremiumOnly is false || item.IsPremium)
            .Where(item => ownedStoreItems.Contains(GetOwnedItemCode(item)) is false)
            .OrderByDescending(item => item.ID)
            .ToList();
    }

    private static PlinkoPrizeTier RollRandomTier()
    {
        int totalWeight = Configuration.PrizeTiers.Sum(tier => tier.Weight);
        int roll = Random.Shared.Next(totalWeight);
        int cumulativeWeight = 0;

        foreach (PlinkoPrizeTier tier in Configuration.PrizeTiers)
        {
            cumulativeWeight += tier.Weight;

            if (roll < cumulativeWeight)
                return tier;
        }

        // Fallback (Should Never Happen)
        return Configuration.PrizeTiers.Last();
    }

    /// <summary>
    /// Constructs the owned-item code for a store item by combining its type prefix with its code.
    /// </summary>
    private static string GetOwnedItemCode(StoreItem item)
    {
        string prefix = GetProductTypeAbbreviation(item);

        return string.IsNullOrEmpty(prefix) ? item.Code : $"{prefix}.{item.Code}";
    }

    /// <summary>
    /// Maps a store item's type to its short prefix code used in the owned-items list and product type responses.
    /// </summary>
    private static string GetProductTypeAbbreviation(StoreItem item) => item.StoreItemType switch
    {
        StoreItemType.ChatNameColour  => "cc",
        StoreItemType.ChatSymbol      => "cs",
        StoreItemType.AccountIcon     => "ai",
        StoreItemType.AlternativeAvatar => "aa",
        StoreItemType.AnnouncerVoice => "av",
        StoreItemType.Taunt           => "t",
        StoreItemType.Courier         => "c",
        StoreItemType.Miscellaneous   => "m",
        StoreItemType.Ward            => "w",
        StoreItemType.Enhancement     => "en",
        StoreItemType.Creep           => "cr",
        StoreItemType.TeleportEffect  => "te",
        StoreItemType.SelectionCircle => "sc",
        _                             => string.Empty
    };

    /// <summary>
    /// Maps a store item's type to its human-readable display name used in the product type response field.
    /// </summary>
    private static string GetProductTypeDisplayName(StoreItem item) => item.StoreItemType switch
    {
        StoreItemType.ChatNameColour    => "Name Color",
        StoreItemType.ChatSymbol        => "Chat Symbol",
        StoreItemType.AccountIcon       => "Account Icon",
        StoreItemType.AlternativeAvatar => "Alt Avatar",
        StoreItemType.AnnouncerVoice    => "Alt Announcement",
        StoreItemType.Taunt             => "Taunt",
        StoreItemType.Courier           => "Courier",
        StoreItemType.Miscellaneous     => "Misc",
        StoreItemType.Ward              => "Ward",
        StoreItemType.Enhancement       => "Enhancement",
        StoreItemType.Creep             => "Creep",
        StoreItemType.TeleportEffect    => "Teleportation Effect",
        StoreItemType.SelectionCircle   => "Selection Circle",
        _                              => "Misc"
    };

    #endregion
}
