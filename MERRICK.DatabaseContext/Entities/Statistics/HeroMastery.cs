namespace MERRICK.DatabaseContext.Entities.Statistics;

[Index(nameof(AccountID), nameof(HeroIdentifier), IsUnique = true)]
public class HeroMastery
{
    [Key]
    public int ID { get; set; }

    public int AccountID { get; set; }

    [ForeignKey(nameof(AccountID))]
    public required Account Account { get; set; }

    [MaxLength(64)]
    public required string HeroIdentifier { get; set; }

    public int MasteryExperience { get; set; } = 0;

    public List<int> ClaimedRewardLevels { get; set; } = [];

    public DateTimeOffset TimestampLastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
