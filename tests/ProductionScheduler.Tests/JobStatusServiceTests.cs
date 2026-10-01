using ProductionScheduler.Scheduling;
using Xunit;

namespace ProductionScheduler.Tests;

public class JobStatusServiceTests
{
    private static readonly DateTime Now = new(2026, 10, 1, 9, 0, 0);

    [Fact]
    public void IsAtRisk_GapWithinBuffer_IsTrue()
    {
        // Due end-of-day on the 2nd; estimated completion the morning of the 2nd — under 24h left.
        var dueDate = new DateOnly(2026, 10, 2);
        var estimatedCompletion = new DateTime(2026, 10, 2, 6, 0, 0);

        Assert.True(JobStatusService.IsAtRisk(estimatedCompletion, dueDate, "ready", Now, bufferMinutes: 1440));
    }

    [Fact]
    public void IsAtRisk_GapBeyondBuffer_IsFalse()
    {
        var dueDate = new DateOnly(2026, 10, 10);
        var estimatedCompletion = new DateTime(2026, 10, 2, 6, 0, 0);

        Assert.False(JobStatusService.IsAtRisk(estimatedCompletion, dueDate, "ready", Now, bufferMinutes: 1440));
    }

    // Regression: a schedule whose estimated completion falls AFTER the due date gives a NEGATIVE gap,
    // which satisfies "<= bufferMinutes" by accident — found live when a schedule recalculation pushed
    // a job's estimated completion three days past its due date and it still showed as merely "at risk"
    // rather than something worse. At risk means tight-but-still-makeable, not already-blown.
    [Fact]
    public void IsAtRisk_EstimatedCompletionAlreadyPastDueDate_IsFalse()
    {
        var dueDate = new DateOnly(2026, 10, 2);
        var estimatedCompletion = new DateTime(2026, 10, 5, 7, 52, 0); // 3 days after the due date

        Assert.False(JobStatusService.IsAtRisk(estimatedCompletion, dueDate, "ready", Now, bufferMinutes: 1440));
    }

    [Fact]
    public void IsAtRisk_AlreadyLate_IsFalse()
    {
        // Dashboard tiles shouldn't double-count: a job already late belongs in "late", not "at risk".
        var dueDate = new DateOnly(2026, 9, 1);
        var estimatedCompletion = new DateTime(2026, 9, 1, 6, 0, 0);

        Assert.False(JobStatusService.IsAtRisk(estimatedCompletion, dueDate, "ready", Now, bufferMinutes: 1440));
    }

    [Fact]
    public void IsAtRisk_CompletedJob_IsFalse()
    {
        var dueDate = new DateOnly(2026, 10, 2);
        var estimatedCompletion = new DateTime(2026, 10, 2, 6, 0, 0);

        Assert.False(JobStatusService.IsAtRisk(estimatedCompletion, dueDate, "completed", Now, bufferMinutes: 1440));
    }

    [Fact]
    public void IsBehindSchedule_EstimatedCompletionInThePast_IsTrue()
    {
        Assert.True(JobStatusService.IsBehindSchedule(Now.AddHours(-1), "in-production", Now));
    }

    [Fact]
    public void IsBehindSchedule_EstimatedCompletionInTheFuture_IsFalse()
    {
        Assert.False(JobStatusService.IsBehindSchedule(Now.AddHours(1), "in-production", Now));
    }

    [Fact]
    public void IsBehindSchedule_CompletedJob_IsFalse()
    {
        // Even if its estimate was in the past, a completed job isn't "behind" anything any more.
        Assert.False(JobStatusService.IsBehindSchedule(Now.AddDays(-1), "completed", Now));
    }
}
