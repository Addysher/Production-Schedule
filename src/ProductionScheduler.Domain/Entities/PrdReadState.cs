namespace ProductionScheduler.Domain.Entities;

public class PrdReadState
{
    public int StationId { get; set; }
    public Station Station { get; set; } = null!;
    public DateOnly FileDate { get; set; }
    public int LastLineRead { get; set; }
    public DateTime LastProcessedAt { get; set; }
}
