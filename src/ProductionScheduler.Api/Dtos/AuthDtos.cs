namespace ProductionScheduler.Api.Dtos;

public record LoginRequest(string Username, string Password);

// FactoryId: a Kiosk account's factory, so it opens straight onto that factory's display.
public record LoginResponse(string Token, string Username, string Role, int? FactoryId);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
