namespace ProductionScheduler.Api.Dtos;

public record OperatorDto(int Id, string Name, int? ShiftId, bool IsActive);

public record UpsertOperatorRequest(string Name, int? ShiftId, bool IsActive);
