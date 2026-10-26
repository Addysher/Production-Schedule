namespace ProductionScheduler.Api.Dtos;

public record LoginRequest(string Username, string Password);

// FactoryId: a Kiosk account's factory, so it opens straight onto that factory's display.
public record LoginResponse(string Token, string Username, string Role, int? FactoryId);

// The signed-in account as it is now (role and kiosk factory can change while a session lasts).
public record MeResponse(string Username, string Role, int? FactoryId);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
