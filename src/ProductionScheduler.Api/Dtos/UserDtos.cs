namespace ProductionScheduler.Api.Dtos;

public record UserDto(int Id, string Username, string Role, bool IsActive, DateTime CreatedAt, DateTime? LastLoginAt, int? FactoryId, string? FactoryName);

// FactoryId: required for a Kiosk account (the factory its display shows), ignored for every other role.
public record CreateUserRequest(string Username, string Password, string Role, int? FactoryId = null);

public record UpdateUserRequest(string Role, bool IsActive, int? FactoryId = null);

public record ResetPasswordRequest(string NewPassword);
