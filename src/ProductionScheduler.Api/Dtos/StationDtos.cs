namespace ProductionScheduler.Api.Dtos;

// EqaPerHour: what the line makes per working hour. TodayTargetEqa: that × today's working hours (with any
// one-off change). TodayChange: today's one-off change for the line, described ("Closed — Easter Monday").
public record StationDto(
    int Id, string Name, string ComponentType, string? PrdPath, string? MpsPath, bool IsActive,
    int DisplayOrder, decimal? EqaPerHour, string? ArchivePath, int? FactoryId, decimal? TodayTargetEqa, string? TodayChange);

public record CreateStationRequest(
    string Name, string ComponentType, string? PrdPath, string? MpsPath, int DisplayOrder,
    decimal? EqaPerHour, string? ArchivePath, int? FactoryId = null);

public record UpdateStationRequest(
    string Name, string ComponentType, string? PrdPath, string? MpsPath, bool IsActive, int DisplayOrder,
    decimal? EqaPerHour, string? ArchivePath, int? FactoryId = null);

// A one-off change to a line's day: overtime hours (start and/or end), or closed.
public record CapacityOverrideDto(int Id, int StationId, DateOnly OverrideDate, bool IsClosed, TimeOnly? StartTime, TimeOnly? EndTime, string? Note);

public record UpsertCapacityOverrideRequest(DateOnly OverrideDate, bool IsClosed, TimeOnly? StartTime, TimeOnly? EndTime, string? Note);
