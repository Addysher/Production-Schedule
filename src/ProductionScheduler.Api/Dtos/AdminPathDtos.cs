namespace ProductionScheduler.Api.Dtos;

public record ProductDto(int Id, string Name, string? PoolPath, string? ArchivePath, bool IsActive);

public record UpsertProductRequest(string Name, string? PoolPath, string? ArchivePath, bool IsActive);

public record BatchcutWatchPathDto(int Id, string Name, string UncPath, bool IsActive);

public record UpsertBatchcutWatchPathRequest(string Name, string UncPath, bool IsActive);
