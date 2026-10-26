namespace ProductionScheduler.Api.Dtos;

public record StationDto(
    int Id, string Name, string ComponentType, string? PrdPath, string? MpsPath, bool IsActive,
    int DisplayOrder, decimal? DailyEqATarget, string? ArchivePath, int? FactoryId);

public record CreateStationRequest(
    string Name, string ComponentType, string? PrdPath, string? MpsPath, int DisplayOrder,
    decimal? DailyEqATarget, string? ArchivePath, int? FactoryId = null);

public record UpdateStationRequest(
    string Name, string ComponentType, string? PrdPath, string? MpsPath, bool IsActive, int DisplayOrder,
    decimal? DailyEqATarget, string? ArchivePath, int? FactoryId = null);

public record CapacityOverrideDto(int Id, int StationId, DateOnly OverrideDate, decimal EqATarget);

public record UpsertCapacityOverrideRequest(DateOnly OverrideDate, decimal EqATarget);
