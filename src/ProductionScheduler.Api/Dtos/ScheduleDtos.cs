namespace ProductionScheduler.Api.Dtos;

public record ScheduleItemDto(
    int Id, string JobRef, string? ClientName, int StationId, string StationName, string? ComponentType,
    DateOnly ScheduleDate, DateTime PlannedStart, DateTime PlannedEnd, decimal PlannedEqA, int MembersTotal,
    DateTime? ActualStart, DateTime? ActualEnd, decimal EqaCompleted, int MembersCompleted,
    bool CreepDetected, DateTime? CreepApprovedAt, string JobStatus);
