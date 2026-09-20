namespace ProductionScheduler.Api.Dtos;

public record MaintenanceLogDto(
    int Id, string TaskName, string? JobRef, string? SourcePath, string? DestPath,
    string Outcome, string? Message, DateTime RunAt);
