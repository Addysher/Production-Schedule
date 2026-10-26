namespace ProductionScheduler.Api.Dtos;

// A quote line item: a named kind of work for a product (v3.0: its speed now comes from the product's lines).
public record JobTypeDto(int Id, string Name, string? ComponentType, bool IsActive);

public record UpsertJobTypeRequest(string Name, string? ComponentType, bool IsActive);
