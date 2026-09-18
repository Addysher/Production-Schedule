namespace ProductionScheduler.Domain.Entities;

public class Precedence
{
    public string PredecessorJobRef { get; set; } = null!;
    public Job PredecessorJob { get; set; } = null!;
    public string SuccessorJobRef { get; set; } = null!;
    public Job SuccessorJob { get; set; } = null!;
}
