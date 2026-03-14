using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using KONGOR.MasterServer.Models.Plinko;
using KONGOR.MasterServer.Configuration.Store;

namespace KONGOR.MasterServer.Controllers.Casino;

[ApiController]
[Route("master")]
public class PlinkoController(MerrickContext databaseContext, IDatabase distributedCache, ILogger<PlinkoController> logger) : ControllerBase
{
    private MerrickContext MerrickContext { get; } = databaseContext;
    private IDatabase DistributedCache { get; } = distributedCache;
    private ILogger Logger { get; } = logger;

    private static readonly Random RandomInstance = new ();

    private static PlinkoConfiguration PlinkoConfig => JSONConfiguration.PlinkoConfiguration;
    private static StoreItemsConfiguration StoreItems => JSONConfiguration.StoreItemsConfiguration;

    [HttpPost("casino/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> GetPlinkoInfo()
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

        PlinkoConfiguration plinko = PlinkoConfig;
        List<PlinkoTier> tiers = plinko.Tiers;

        List<string> tiersArray = GetTiersDisplayOrder(tiers);
        Logger.LogInformation(@"[Plinko] Account ""{AccountName}"" - Tiers: {Tiers}, GoldCost: {GoldCost}, TicketCost: {TicketCost}",
            accountName, string.Join(",", tiersArray), plinko.GoldCost, plinko.TicketCost);

        // Client expects "tiers" as PHP array mapping UI slot index to tier number (see HoN-Revival plinko docs).
        OrderedDictionary response = new ()
        {
            ["status_code"] = 1,
            ["tiers"] = tiersArray,
            ["user_tickets"] = user.PlinkoTickets,
            ["gold_cost"] = plinko.GoldCost,
            ["ticket_cost"] = plinko.TicketCost,
            ["amount_of_products"] = GetAmountOfProducts(tiers, user),
            ["last_update_time"] = GetLastUpdateTimes(tiers),
            ["user_gold"] = user.GoldCoins,
            ["silver"] = user.SilverCoins
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    [HttpPost("casino/drop/")]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> PlinkoDrop()
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

        PlinkoConfiguration plinko = PlinkoConfig;

        if (currency == "tickets")
        {
            if (user.PlinkoTickets < plinko.TicketCost)
            {
                return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
                {
                    ["status_code"] = 0,
                    ["error_message"] = "Not enough tickets"
                }));
            }

            user.PlinkoTickets -= plinko.TicketCost;
        }
        else if (currency == "gold")
        {
            if (user.GoldCoins < plinko.GoldCost)
            {
                return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
                {
                    ["status_code"] = 0,
                    ["error_message"] = "Not enough gold coins"
                }));
            }

            user.GoldCoins -= plinko.GoldCost;
        }
        else
        {
            return BadRequest($@"Invalid Currency ""{currency}""");
        }

        int winningTier = DetermineWinningTier(plinko.Tiers);
        PlinkoTier tierConfig = plinko.Tiers[winningTier - 1];

        Dictionary<string, object> response;

        if (tierConfig.IsTicketTier)
        {
            user.PlinkoTickets += tierConfig.TicketAmount;

            response = new Dictionary<string, object>
            {
                ["user_tickets"] = user.PlinkoTickets,
                ["user_gold"] = user.GoldCoins,
                ["status_code"] = 1,
                ["random_tier"] = winningTier,
                ["product_id"] = -1,
                ["product_name"] = "Ticket",
                ["product_type"] = "Ticket",
                ["product_path"] = "Ticket",
                ["products_exhausted"] = false,
                ["ticket_amount"] = tierConfig.TicketAmount
            };
        }
        else
        {
            bool productsExhausted = AreProductsExhausted(tierConfig, user);

            if (productsExhausted)
            {
                user.PlinkoTickets += tierConfig.TicketAmount;

                response = new Dictionary<string, object>
                {
                    ["user_tickets"] = user.PlinkoTickets,
                    ["user_gold"] = user.GoldCoins,
                    ["status_code"] = 1,
                    ["random_tier"] = winningTier,
                    ["product_id"] = -1,
                    ["product_name"] = "Ticket",
                    ["product_type"] = "Ticket",
                    ["product_path"] = "Ticket",
                    ["products_exhausted"] = true,
                    ["ticket_amount"] = tierConfig.TicketAmount
                };
            }
            else
            {
                PlinkoProduct? product = SelectRandomProduct(tierConfig);

                if (product is null)
                {
                    user.PlinkoTickets += tierConfig.TicketAmount;

                    response = new Dictionary<string, object>
                    {
                        ["user_tickets"] = user.PlinkoTickets,
                        ["user_gold"] = user.GoldCoins,
                        ["status_code"] = 1,
                        ["random_tier"] = winningTier,
                        ["product_id"] = -1,
                        ["product_name"] = "Ticket",
                        ["product_type"] = "Ticket",
                        ["product_path"] = "Ticket",
                        ["products_exhausted"] = false,
                        ["ticket_amount"] = tierConfig.TicketAmount
                    };
                }
                else
                {
                    if (user.OwnedStoreItems.Contains(product.PrefixedCode).Equals(false))
                    {
                        user.OwnedStoreItems.Add(product.PrefixedCode);
                    }

                    response = new Dictionary<string, object>
                    {
                        ["user_tickets"] = user.PlinkoTickets,
                        ["user_gold"] = user.GoldCoins,
                        ["status_code"] = 1,
                        ["random_tier"] = winningTier,
                        ["product_id"] = product.ID,
                        ["product_name"] = GetProductCode(product),
                        ["product_type"] = MapStoreItemTypeToCategoryName(product.StoreItemType),
                        ["product_path"] = GetProductDisplayPath(product),
                        ["products_exhausted"] = false,
                        ["ticket_amount"] = 0
                    };
                }
            }
        }

        await MerrickContext.SaveChangesAsync();

        return Ok(PhpSerialization.Serialize(response));
    }

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

        PlinkoConfiguration plinko = PlinkoConfig;
        List<PlinkoExchangeItem> exchangeItems = plinko.ExchangeItems;

        List<Dictionary<string, object>> items = [];

        foreach (PlinkoExchangeItem exchangeItem in exchangeItems)
        {
            StoreItem? storeItem = StoreItems.GetByID(exchangeItem.ProductID);

            if (storeItem is null)
                continue;

            items.Add(new Dictionary<string, object>
            {
                ["id"] = items.Count + 1,
                ["cost"] = exchangeItem.TicketCost,
                ["product_id"] = storeItem.ID,
                ["name"] = storeItem.Code,
                ["type"] = MapStoreItemTypeToCategoryName(storeItem.StoreItemType),
                ["local_path"] = storeItem.Resource
            });
        }

        Dictionary<string, object> response = new ()
        {
            ["status_code"] = 51,
            ["items"] = items,
            ["user_tickets"] = user.PlinkoTickets
        };

        return Ok(PhpSerialization.Serialize(response));
    }

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

        PlinkoConfiguration plinko = PlinkoConfig;

        PlinkoExchangeItem? exchangeItem = plinko.ExchangeItems.FirstOrDefault(item => item.ProductID == productId);

        if (exchangeItem is null)
        {
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
            {
                ["status_code"] = 0,
                ["error_message"] = "Invalid product"
            }));
        }

        if (user.PlinkoTickets < exchangeItem.TicketCost)
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

        user.PlinkoTickets -= exchangeItem.TicketCost;

        if (user.OwnedStoreItems.Contains(storeItem.PrefixedCode).Equals(false))
        {
            user.OwnedStoreItems.Add(storeItem.PrefixedCode);
        }

        await MerrickContext.SaveChangesAsync();

        Dictionary<string, object> response = new ()
        {
            ["status_code"] = 51,
            ["tickets_remaining"] = user.PlinkoTickets,
            ["grabBag"] = false
        };

        return Ok(PhpSerialization.Serialize(response));
    }

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

        if (int.TryParse(tierIdString, out int tierId).Equals(false))
            return BadRequest(@"Invalid Value For Form Parameter ""tier_id""");

        string? targetIndexString = Request.Form["target_index"];

        if (targetIndexString is null)
            return BadRequest(@"Missing Value For Form Parameter ""target_index""");

        if (int.TryParse(targetIndexString, out int targetIndex).Equals(false))
            return BadRequest(@"Invalid Value For Form Parameter ""target_index""");

        (bool isValid, string? accountName) = await DistributedCache.ValidateAccountSessionCookie(cookie);

        if (isValid.Equals(false) || accountName is null)
            return Unauthorized($@"Unrecognized Cookie ""{cookie}""");

        PlinkoConfiguration plinko = PlinkoConfig;

        // Client sends tier number (1=Diamond, 2=Gold, 3=Silver, 4=Bronze, 5=60 tickets, 6=30 tickets).
        if (tierId < 1 || tierId > plinko.Tiers.Count)
        {
            return BadRequest($@"Invalid Tier ID ""{tierId}""");
        }

        PlinkoTier tier = plinko.Tiers[tierId - 1];

        if (tier.IsTicketTier)
        {
            return Ok(PhpSerialization.Serialize(new Dictionary<string, object>
            {
                ["tier_id"] = tierId,
                ["items_amount"] = 0,
                ["first_item_index"] = 1,
                ["target_index"] = targetIndex
            }));
        }

        List<PlinkoProduct> products = tier.Products;

        int itemsPerPage = 4;
        int totalItems = products.Count;

        // targetIndex is the 1-based item index to start from
        targetIndex = Math.Max(1, Math.Min(targetIndex, Math.Max(1, totalItems)));

        // Convert to 0-based index for slicing
        int startIndex = targetIndex - 1;
        int endIndex = Math.Min(startIndex + itemsPerPage, totalItems);

        List<PlinkoProduct> pageProducts = products.Count > 0 && startIndex < totalItems
            ? products.GetRange(startIndex, endIndex - startIndex)
            : [];

        List<string> productNames = [];
        List<string> productTypes = [];
        List<string> productPaths = [];
        List<string> productIds = [];

        foreach (PlinkoProduct product in pageProducts)
        {
            productNames.Add(GetProductCode(product));
            productTypes.Add(MapStoreItemTypeToShortCode(product.StoreItemType));
            productPaths.Add(GetProductDisplayPath(product));
            productIds.Add(product.ID.ToString());
        }

        Dictionary<string, object> response = new ()
        {
            ["tier_id"] = tierId,
            ["items_amount"] = totalItems,
            ["first_item_index"] = startIndex + 1,
            ["target_index"] = targetIndex,
            ["product_names"] = string.Join(",", productNames),
            ["product_types"] = string.Join(",", productTypes),
            ["product_paths"] = string.Join(",", productPaths),
            ["product_ids"] = string.Join(",", productIds)
        };

        return Ok(PhpSerialization.Serialize(response));
    }

    #region Helper Methods

    /// <summary>
    ///     Returns the product code (e.g. "Hero_Pyromancer.Female") that the client uses to resolve the icon/model. HoN Revival viewchest response uses codes in "product_names", not display names.
    /// </summary>
    private static string GetProductCode(PlinkoProduct product)
    {
        StoreItem? storeItem = StoreItems.GetByID(product.ID);
        if (storeItem is not null)
            return storeItem.Code;
        int dotIndex = product.PrefixedCode.IndexOf('.');
        return dotIndex >= 0 ? product.PrefixedCode[(dotIndex + 1)..] : product.PrefixedCode;
    }

    /// <summary>
    ///     Returns the path the client uses to load the product icon/model. Prefers the store item "Resource" when the product exists in the store so the plinko UI loads the same texture as the store.
    /// </summary>
    private static string GetProductDisplayPath(PlinkoProduct product)
    {
        StoreItem? storeItem = StoreItems.GetByID(product.ID);
        return storeItem?.Resource ?? product.LocalPath;
    }

    /// <summary>
    ///     Returns tier numbers in UI display order (slot 0 = first bucket, etc.). Maps to PHP array in GetPlinkoInfo response.
    /// </summary>
    private static List<string> GetTiersDisplayOrder(List<PlinkoTier> tiers)
    {
        int[] displayOrder = new int[] { 5, 3, 4, 1, 6, 2 };
        return displayOrder.Take(tiers.Count).Select(tierNumber => tierNumber.ToString()).ToList();
    }

    private static string GetAmountOfProducts(List<PlinkoTier> tiers, User user)
    {
        // Display order matches GetTiersDisplayOrder: 5,3,4,1,6,2
        int[] displayOrder = new int[] { 5, 3, 4, 1, 6, 2 };
        List<string> amounts = [];

        foreach (int tierIndex in displayOrder.Take(tiers.Count))
        {
            PlinkoTier tier = tiers[tierIndex - 1];
            
            if (tier.IsTicketTier)
            {
                amounts.Add("0");
            }
            else
            {
                int availableCount = tier.Products.Count(p => !user.OwnedStoreItems.Contains(p.PrefixedCode));
                amounts.Add(availableCount.ToString());
            }
        }

        return string.Join(",", amounts);
    }

    private static string GetLastUpdateTimes(List<PlinkoTier> tiers)
    {
        // Display order matches GetTiersDisplayOrder: 5,3,4,1,6,2
        int[] displayOrder = new int[] { 5, 3, 4, 1, 6, 2 };
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return string.Join(",", displayOrder.Take(tiers.Count).Select(_ => now.ToString()));
    }

    private static int DetermineWinningTier(List<PlinkoTier> tiers)
    {
        double roll = RandomInstance.NextDouble() * 100;

        double cumulativeProbability = 0;

        for (int i = 0; i < tiers.Count; i++)
        {
            cumulativeProbability += tiers[i].Probability;

            if (roll < cumulativeProbability)
            {
                return i + 1;
            }
        }

        return tiers.Count;
    }

    private static bool AreProductsExhausted(PlinkoTier tier, User user)
    {
        if (tier.IsTicketTier)
            return false;

        return tier.Products.All(product => user.OwnedStoreItems.Contains(product.PrefixedCode));
    }

    private static PlinkoProduct? SelectRandomProduct(PlinkoTier tier)
    {
        if (tier.IsTicketTier || tier.Products.Count == 0)
            return null;

        return tier.Products[RandomInstance.Next(tier.Products.Count)];
    }

    private static string MapStoreItemTypeToCategoryName(StoreItemType type) => type switch
    {
        StoreItemType.AlternativeAvatar  => "Alt Avatar",
        StoreItemType.AnnouncerVoice    => "Alt Announcement",
        StoreItemType.Courier           => "Couriers",
        StoreItemType.Hero              => "Hero",
        StoreItemType.Ward              => "Ward",
        StoreItemType.Taunt             => "Taunt",
        StoreItemType.Miscellaneous     => "Misc",
        StoreItemType.EarlyAccessProduct => "EAP",
        StoreItemType.ChatNameColour    => "Name Color",
        StoreItemType.ChatSymbol        => "Symbol",
        StoreItemType.AccountIcon       => "Account Icon",
        StoreItemType.Enhancement       => "Enhancement",
        StoreItemType.Mastery           => "Mastery",
        _                               => "Misc"
    };

    private static string MapStoreItemTypeToShortCode(StoreItemType type) => type switch
    {
        StoreItemType.AlternativeAvatar  => "aa",
        StoreItemType.AnnouncerVoice    => "av",
        StoreItemType.Courier           => "cc",
        StoreItemType.Hero              => "he",
        StoreItemType.Ward              => "wd",
        StoreItemType.Taunt             => "t",
        StoreItemType.Miscellaneous     => "mi",
        StoreItemType.EarlyAccessProduct => "ea",
        StoreItemType.ChatNameColour    => "nc",
        StoreItemType.ChatSymbol        => "sy",
        StoreItemType.AccountIcon       => "ai",
        StoreItemType.Enhancement       => "en",
        StoreItemType.Mastery           => "ma",
        _                               => "mi"
    };

    #endregion
}
