namespace KONGOR.MasterServer.Configuration;

using KONGOR.MasterServer.Configuration.Economy;
using KONGOR.MasterServer.Configuration.Mastery;
using KONGOR.MasterServer.Configuration.Matchmaking;
using KONGOR.MasterServer.Configuration.Store;
using KONGOR.MasterServer.Models.Plinko;

public static class JSONConfiguration
{
    private static readonly string BasePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty, "Configuration");

    private static readonly string EconomyConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Economy", "EconomyConfiguration.json"));
    private static readonly string MatchmakingConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Matchmaking", "MatchmakingConfiguration.json"));
    private static readonly string MasteryRewardsConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Mastery", "MasteryRewardsConfiguration.json"));
    private static readonly string StoreItemsConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Store", "StoreItemsConfiguration.json"));
    private static readonly string DailySpecialsConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Store", "DailySpecialsConfiguration.json"));
    private static readonly string FeaturedItemsConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Store", "FeaturedItemsConfiguration.json"));
    private static readonly string AnnouncementsConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Announcements", "AnnouncementsConfiguration.json"));
    private static readonly string PlinkoConfigurationJSON = File.ReadAllText(Path.Combine(BasePath, "Plinko", "PlinkoConfiguration.json"));

    public static readonly EconomyConfiguration EconomyConfiguration = JsonSerializer.Deserialize<EconomyConfiguration>(EconomyConfigurationJSON)
        ?? throw new NullReferenceException("Economy Configuration Is NULL");

    public static readonly MatchmakingConfiguration MatchmakingConfiguration = JsonSerializer.Deserialize<MatchmakingConfiguration>(MatchmakingConfigurationJSON)
        ?? throw new NullReferenceException("Matchmaking Configuration Is NULL");

    public static readonly MasteryRewardsConfiguration MasteryRewardsConfiguration = JsonSerializer.Deserialize<MasteryRewardsConfiguration>(MasteryRewardsConfigurationJSON)
        ?? throw new NullReferenceException("Mastery Rewards Configuration Is NULL");

    public static readonly StoreItemsConfiguration StoreItemsConfiguration = new (JsonSerializer.Deserialize<List<StoreItem>>(StoreItemsConfigurationJSON)
        ?? throw new NullReferenceException("Store Item Configuration Is NULL"));

    public static readonly DailySpecialsConfiguration DailySpecialsConfiguration = JsonSerializer.Deserialize<DailySpecialsConfiguration>(DailySpecialsConfigurationJSON)
        ?? throw new NullReferenceException("Daily Specials Configuration Is NULL");

    public static readonly FeaturedItemsConfiguration FeaturedItemsConfiguration = JsonSerializer.Deserialize<FeaturedItemsConfiguration>(FeaturedItemsConfigurationJSON)
        ?? throw new NullReferenceException("Featured Items Configuration Is NULL");

    public static readonly AnnouncementsConfiguration AnnouncementsConfiguration = JsonSerializer.Deserialize<AnnouncementsConfiguration>(AnnouncementsConfigurationJSON)
        ?? throw new NullReferenceException("Announcements Configuration Is NULL");

    public static readonly PlinkoConfiguration PlinkoConfiguration = JsonSerializer.Deserialize<PlinkoConfiguration>(PlinkoConfigurationJSON)
        ?? throw new NullReferenceException("Plinko Configuration Is NULL");

    static JSONConfiguration()
    {
        // New format uses dynamic filtering by gold cost in the controller - no pre-resolution needed
    }
}
