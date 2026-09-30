namespace ProductionScheduler.Domain.Entities;

public class JobMember
{
    public int Id { get; set; }
    public string JobRef { get; set; } = null!;
    public Job Job { get; set; } = null!;
    public string MemberId { get; set; } = null!; // e.g. 'E17', 'I31'
    public string? ComponentType { get; set; }
    public string? MemberCategory { get; set; } // e.g. 'Ext Load Bearing'

    // The two components of EqaValue, named for the two Sapphire "Activities" they come from
    // (see SchImportService.ParseFile). Field 13 (SetupJig) is 0 for component types with a single
    // activity (WallFrames' FramingMachine, say); field 14 (ProcessJig) is the field this project
    // originally — wrongly — treated as a linear length in metres.
    public decimal SetupEqA { get; set; }
    public decimal ProcessEqA { get; set; }

    public int? WidthMm { get; set; }
    public int? HeightMm { get; set; }
    public int Batch { get; set; }
    public int Sequence { get; set; }
    public int SubSequence { get; set; }
    public decimal EqaValue { get; set; }

    // Set by PRD reader on Trusses Made event
    public bool Done { get; set; }
    public DateTime? CompletedAt { get; set; }
    public decimal? CompletedEqA { get; set; }

    public string? Raw2 { get; set; }
}
