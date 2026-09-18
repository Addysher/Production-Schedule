namespace ProductionScheduler.Api.Dtos;

public record SchImportPathDto(int Id, string Name, string UncPath, string? ComponentType, bool IsActive);

public record UpsertSchImportPathRequest(string Name, string UncPath, string? ComponentType, bool IsActive);

public record BatchcutWatchPathDto(int Id, string Name, string UncPath, bool IsActive);

public record UpsertBatchcutWatchPathRequest(string Name, string UncPath, bool IsActive);
