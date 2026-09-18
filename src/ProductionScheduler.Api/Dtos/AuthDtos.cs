namespace ProductionScheduler.Api.Dtos;

public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, string Username, string Role);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);
