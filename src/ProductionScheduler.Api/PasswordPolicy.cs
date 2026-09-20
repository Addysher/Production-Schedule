namespace ProductionScheduler.Api;

// Deliberately minimal: the doc specifies local accounts but no password rules. This only stops the
// obviously-wrong cases (empty / trivially short) — tighten it if the factory has a policy.
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static string? Validate(string? password) =>
        string.IsNullOrEmpty(password) || password.Length < MinLength
            ? $"Password must be at least {MinLength} characters."
            : null;
}
