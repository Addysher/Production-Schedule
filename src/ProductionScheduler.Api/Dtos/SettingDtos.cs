namespace ProductionScheduler.Api.Dtos;

public record SettingDto(string Key, string Value, DateTime UpdatedAt);

// Nullable so that an empty string is accepted (a non-nullable string is implicitly [Required], which rejects "").
public record UpdateSettingRequest(string? Value);
