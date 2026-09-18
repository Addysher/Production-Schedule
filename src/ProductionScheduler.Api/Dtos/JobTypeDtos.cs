namespace ProductionScheduler.Api.Dtos;

public record JobTypeDto(int Id, string Name, string? ComponentType, decimal MinsPerUnit, bool IsActive);

public record UpsertJobTypeRequest(string Name, string? ComponentType, decimal MinsPerUnit, bool IsActive);
