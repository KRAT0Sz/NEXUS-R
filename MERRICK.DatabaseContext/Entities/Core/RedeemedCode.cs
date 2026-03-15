namespace MERRICK.DatabaseContext.Entities.Core;

[Index(nameof(AccountID), nameof(Code), IsUnique = true)]
public class RedeemedCode
{
    [Key]
    public int ID { get; set; }

    public int AccountID { get; set; }

    [ForeignKey(nameof(AccountID))]
    public required Account Account { get; set; }

    [MaxLength(50)]
    public required string Code { get; set; }

    public DateTimeOffset TimestampRedeemed { get; set; } = DateTimeOffset.UtcNow;
}
