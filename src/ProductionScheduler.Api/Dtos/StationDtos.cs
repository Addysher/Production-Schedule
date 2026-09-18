namespace ProductionScheduler.Api.Dtos;

public record StationDto(
    int Id, string Name, string ComponentType, string? UncPath, bool IsActive,
    int DisplayOrder, decimal? DailyEqATarget, string? ArchivePath);

public record CreateStationRequest(
    string Name, string ComponentType, string? UncPath, int DisplayOrder,
    decimal? DailyEqATarget, string? ArchivePath);

public record UpdateStationRequest(
    string Name, string ComponentType, string? UncPath, bool IsActive, int DisplayOrder,
    decimal? DailyEqATarget, string? ArchivePath);

public record CapacityOverrideDto(int Id, int StationId, DateOnly OverrideDate, decimal EqATarget);

public record UpsertCapacityOverrideRequest(DateOnly OverrideDate, decimal EqATarget);
