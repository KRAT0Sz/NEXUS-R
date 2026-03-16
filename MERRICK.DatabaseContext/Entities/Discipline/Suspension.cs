namespace MERRICK.DatabaseContext.Entities.Discipline;

public class Suspension
{
    public int ID { get; set; }

    public int? UserID { get; set; }
    public User? User { get; set; }

    public int? AccountID { get; set; }
    public Account? Account { get; set; }

    [Required]
    [StringLength(256)]
    public required string Reason { get; set; }

    public DateTimeOffset TimestampStarted { get; set; }

    public DateTimeOffset? TimestampExpires { get; set; }

    public bool IsPermanent { get; set; }

    public bool IsLifted { get; set; }

    public DateTimeOffset? TimestampLifted { get; set; }

    public int? LiftedByUserID { get; set; }
    public User? LiftedByUser { get; set; }
}
