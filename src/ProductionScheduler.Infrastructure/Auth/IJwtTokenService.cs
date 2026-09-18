using ProductionScheduler.Domain.Entities;

namespace ProductionScheduler.Infrastructure.Auth;

public interface IJwtTokenService
{
    string GenerateToken(User user);
}
