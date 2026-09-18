namespace ProductionScheduler.Api.Dtos;

public record SettingDto(string Key, string Value, DateTime UpdatedAt);

public record UpdateSettingRequest(string Value);
