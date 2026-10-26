namespace ProductionScheduler.Domain.Entities;

public class Job
{
    public int Id { get; set; }
    public string JobRef { get; set; } = null!; // e.g. '59778MWF'

    // The job's product (Products.Name) — set from the pool folder its SCH file arrived in.
    public string? ComponentType { get; set; }

    // From SCH ScheduleJob record
    public string? SchComponentType { get; set; } // pos 3 — kept for reference; doesn't decide anything
    public string? ClientName { get; set; } // pos 4
    public DateOnly? ExpectedDeliveryDate { get; set; } // pos 5
    public DateOnly? EstBookedDeliveryDate { get; set; } // pos 6
    public string? ClientRefNumber { get; set; } // pos 9
    public string? SiteAddress { get; set; } // pos 10
    public string? SiteSuburb { get; set; } // pos 11
    public string? SupervisorName { get; set; } // pos 12: manual entry only
    public string? SupervisorNumber { get; set; } // pos 13: manual entry only
    public DateOnly? JobOrderDate { get; set; } // pos 14: written once on first import
    public TimeOnly? JobOrderTime { get; set; } // pos 15: written once on first import
    public string DesignStatus { get; set; } = "Quote"; // 'Quote' | 'FinalDesign'

    // Reserved SCH fields (stored, never displayed)
    public string? Raw2 { get; set; }
    public string? Raw7 { get; set; }
    public string? Raw8 { get; set; }

    // Scheduler output
    public int? AssignedStationId { get; set; }
    public Station? AssignedStation { get; set; }
    public DateTime? EstimatedStartDate { get; set; }
    public DateTime? EstimatedCompletionDate { get; set; }

    // Production totals (maintained by PRD reader)
    public decimal? TotalEqA { get; set; }
    public decimal CompletedEqA { get; set; }

    // Cut approval
    public DateTime? ApprovedForCuttingAt { get; set; }
    public int? ApprovedByUserId { get; set; }
    public User? ApprovedByUser { get; set; }

    // Files
    public string? SchSourcePath { get; set; }   // where the imported SCH file was archived
    public string? MpsSourcePath { get; set; }   // the product pool folder the job's MPS file waits in
    public int? MpsStationId { get; set; }       // station whose DataIn currently holds the MPS; NULL = still in the pool
    public Station? MpsStation { get; set; }

    // §5.11: where the job's saw-file (MME) bundle is. NULL = no files yet; 'bundling' = collecting in
    // RawFilesPath\_bundling until every member has its file; 'waiting' = complete, in SawFilesDoNotCutPath;
    // 'cutting' = approved, in SawFilesForCuttingPath.
    public string? SawBundleStatus { get; set; }
    public int SawFileCount { get; set; }

    // Manual scheduling override
    public bool IsManuallyScheduled { get; set; }
    public int? ManualStationId { get; set; }
    public Station? ManualStation { get; set; }
    public DateOnly? ManualScheduleDate { get; set; }

    // Manual entry (editable via job modal only)
    public DateOnly? BookedDeliveryDate { get; set; }
    public string? InvoiceNumber { get; set; }
    public bool Delivered { get; set; }
    public DateTime? DeliveredAt { get; set; }

    public DateTime? MpsArchivedAt { get; set; }

    // System
    public string Status { get; set; } = "scheduled"; // see §5.0
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public bool IsArchived { get; set; }
    public DateTime? ArchivedAt { get; set; }

    public ICollection<JobMember> Members { get; set; } = new List<JobMember>();
    public ICollection<ScheduleItem> ScheduleItems { get; set; } = new List<ScheduleItem>();
}
