namespace ProductionScheduler.Api.Dtos;

// One stretch of a job's planned work on one calendar day (a multi-day job has one per day it runs).
public record ScheduleSegmentDto(DateOnly Date, DateTime Start, DateTime End, decimal PlannedEqA);

public record ScheduleItemDto(
    int Id, string JobRef, string? ClientName, int StationId, string StationName, string? ComponentType,
    DateOnly ScheduleDate, DateTime PlannedStart, DateTime PlannedEnd, decimal PlannedEqA, int MembersTotal,
    DateTime? ActualStart, DateTime? ActualEnd, decimal EqaCompleted, int MembersCompleted,
    bool CreepDetected, DateTime? CreepApprovedAt, string JobStatus,
    List<ScheduleSegmentDto> Segments, bool IsFrozen, bool IsTba);

public record BreakDto(string Start, string End);

// What the Calendar screen needs to draw a working day. Readable by every role, unlike /api/settings.
// FreezeUntil: end of the freeze window (NULL = off) — nothing starting before it can be moved or dropped.
// ShiftStartHour/ShiftEndHour are the whole hours the Calendar grid spans; DayStart/DayEnd ("HH:mm") are the
// exact working day, which can end part-way through the last hour (e.g. 14:30).
public record CalendarSettingsDto(int ShiftStartHour, int ShiftEndHour, List<int> WorkingDays, List<BreakDto> Breaks, DateTime? FreezeUntil, string DayStart, string DayEnd);

public record ProductionRowDto(int StationId, string StationName, DateOnly Date, decimal Eqa);

public record CompletionRowDto(DateOnly Date, int Completed, int CompletedLate, int LatePercent, List<string> LateJobRefs);

public record NotificationSummaryDto(int NewJobs, int Errors, DateTime ServerNow);

// The feedback loop (v3.0): per line today, the Eq the plan had made by now vs what the PRD files say was made.
public record LineProgressDto(int StationId, decimal PlannedByNow, decimal MadeToday, decimal PlannedToday);

public record StationProgressDto(int StationId, string JobRef, string? CurrentMemberId, string? NextMemberId, int MembersDone, int MembersTotal);
