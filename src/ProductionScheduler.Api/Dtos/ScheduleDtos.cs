namespace ProductionScheduler.Api.Dtos;

// One stretch of a job's planned work on one calendar day (a multi-day job has one per day it runs).
public record ScheduleSegmentDto(DateOnly Date, DateTime Start, DateTime End, decimal PlannedEqA);

public record ScheduleItemDto(
    int Id, string JobRef, string? ClientName, int StationId, string StationName, string? ComponentType,
    DateOnly ScheduleDate, DateTime PlannedStart, DateTime PlannedEnd, decimal PlannedEqA, int MembersTotal,
    DateTime? ActualStart, DateTime? ActualEnd, decimal EqaCompleted, int MembersCompleted,
    bool CreepDetected, DateTime? CreepApprovedAt, string JobStatus,
    List<ScheduleSegmentDto> Segments);

public record BreakDto(string Start, string End);

// What the Calendar screen needs to draw a working day. Readable by every role, unlike /api/settings.
public record CalendarSettingsDto(int ShiftStartHour, int ShiftEndHour, List<int> WorkingDays, List<BreakDto> Breaks);

public record ProductionRowDto(int StationId, string StationName, DateOnly Date, decimal Eqa);

public record NotificationSummaryDto(int NewJobs, int Errors, DateTime ServerNow);
