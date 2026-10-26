namespace ProductionScheduler.Domain.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string Role { get; set; } = null!; // "Admin" | "Supervisor" | "Viewer" | "Kiosk"
    public bool IsActive { get; set; } = true;
    // Kiosk accounts only: the factory whose display this login shows (one login per factory).
    public int? FactoryId { get; set; }
    public Factory? Factory { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
