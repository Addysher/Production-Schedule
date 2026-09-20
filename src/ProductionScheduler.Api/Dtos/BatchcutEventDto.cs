namespace ProductionScheduler.Api.Dtos;

public record BatchcutEventDto(
    int Id, int BatchcutPathId, string FolderName, string? JobRef,
    DateTime DetectedAt, string Status, string? ErrorMessage);
