namespace ProductionScheduler.Api.Dtos;

public record ShiftBreakDto(int Id, TimeOnly BreakStart, int BreakDurationMins);

public record ShiftDto(int Id, string Name, TimeOnly StartTime, TimeOnly EndTime, bool IsActive, List<ShiftBreakDto> Breaks);

public record UpsertShiftBreakRequest(TimeOnly BreakStart, int BreakDurationMins);

public record UpsertShiftRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime, bool IsActive, List<UpsertShiftBreakRequest> Breaks);
