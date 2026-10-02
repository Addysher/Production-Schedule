using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace ProductionScheduler.Tests.Integration;

// Users.Username lives under SQL Server's default case-insensitive collation (so the unique index
// treats "admin"/"Admin"/"ADMIN" as the same row) — login must still reject a username typed in the
// wrong case rather than silently matching it, even though the password check is already exact.
[Collection(IntegrationCollection.Name)]
public class AuthLoginTests
{
    private readonly IntegrationFixture _f;

    public AuthLoginTests(IntegrationFixture fixture) => _f = fixture;

    [Theory]
    [InlineData("ADMIN")]
    [InlineData("Admin")]
    [InlineData("aDmIn")]
    public async Task Login_CorrectPasswordWrongCaseUsername_IsUnauthorized(string wrongCaseUsername)
    {
        var anon = _f.AnonymousClient();
        var response = await anon.PostAsJsonAsync("/api/auth/login", new { username = wrongCaseUsername, password = "Admin123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Login_ExactCaseUsername_Succeeds()
    {
        var anon = _f.AnonymousClient();
        var response = await anon.PostAsJsonAsync("/api/auth/login", new { username = "admin", password = "Admin123!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
