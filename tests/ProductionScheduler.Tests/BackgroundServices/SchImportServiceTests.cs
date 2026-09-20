using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.BackgroundServices;

public class SchImportServiceTests
{
    // Field values taken directly from the §5.8 worked examples.
    private const string ScheduleJobLine =
        "59778MWF;?;WallFrames;Dale Alcock Homes;18/09/2026;18/09/2026;0;2;38477;#62 Bathers Promenade;Jindalee;;;02/09/2026;14:23:12;FinalDesign";

    private const string ScheduleEntryLine =
        "59778MWF;?;WallFrames;E17;x5;x6;x7;Ext Load Bearing;x9;x10;x11;x12;x13;7.05;x15;x16;4498;2692;x19;x20;x21;False;89.12;x24;1;1;1";

    [Fact]
    public void ParseFile_ScheduleJobLine_ParsesAllDocumentedPositions()
    {
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine });

        Assert.NotNull(result.ScheduleJob);
        var job = result.ScheduleJob!;
        Assert.Equal("59778MWF", job.JobRef);
        Assert.Equal("?", job.Raw2);
        Assert.Equal("WallFrames", job.ComponentType);
        Assert.Equal("Dale Alcock Homes", job.ClientName);
        Assert.Equal(new DateOnly(2026, 9, 18), job.ExpectedDeliveryDate);
        Assert.Equal(new DateOnly(2026, 9, 18), job.EstBookedDeliveryDate);
        Assert.Equal("0", job.Raw7);
        Assert.Equal("2", job.Raw8);
        Assert.Equal("38477", job.ClientRefNumber);
        Assert.Equal("#62 Bathers Promenade", job.SiteAddress);
        Assert.Equal("Jindalee", job.SiteSuburb);
        Assert.Equal(new DateOnly(2026, 9, 2), job.JobOrderDate);
        Assert.Equal(new TimeOnly(14, 23, 12), job.JobOrderTime);
        Assert.Equal("FinalDesign", job.DesignStatus);
    }

    [Fact]
    public void ParseFile_ScheduleEntryLine_ParsesAllDocumentedPositions()
    {
        var result = SchImportService.ParseFile(new[] { ScheduleEntryLine });

        var entry = Assert.Single(result.ScheduleEntries);
        Assert.Equal("59778MWF", entry.JobRef);
        Assert.Equal("?", entry.Raw2);
        Assert.Equal("WallFrames", entry.ComponentType);
        Assert.Equal("E17", entry.MemberId);
        Assert.Equal("Ext Load Bearing", entry.MemberCategory);
        Assert.Equal(7.05m, entry.LinearLengthM);
        Assert.Equal(4498, entry.WidthMm);
        Assert.Equal(2692, entry.HeightMm);
        Assert.False(entry.Done);
        Assert.Equal(89.12m, entry.EqaValue);
        Assert.Equal(1, entry.Batch);
        Assert.Equal(1, entry.Sequence);
        Assert.Equal(1, entry.SubSequence);
    }

    [Fact]
    public void ParseFile_FullFile_ReturnsOneJobAndAllMembers()
    {
        var lines = new[]
        {
            ScheduleJobLine,
            ScheduleEntryLine,
            ScheduleEntryLine.Replace("E17", "I31")
        };

        var result = SchImportService.ParseFile(lines);

        Assert.NotNull(result.ScheduleJob);
        Assert.Equal(2, result.ScheduleEntries.Count);
        Assert.Contains(result.ScheduleEntries, e => e.MemberId == "E17");
        Assert.Contains(result.ScheduleEntries, e => e.MemberId == "I31");
    }

    [Fact]
    public void ParseFile_QuoteDesignStatus_NormalisesCasing()
    {
        var line = ScheduleJobLine.Replace("FinalDesign", "quote");

        var result = SchImportService.ParseFile(new[] { line });

        Assert.Equal("Quote", result.ScheduleJob!.DesignStatus);
    }

    [Fact]
    public void ParseFile_InvalidDesignStatus_RejectsTheLine()
    {
        var line = ScheduleJobLine.Replace("FinalDesign", "InProgress");

        var result = SchImportService.ParseFile(new[] { line });

        Assert.Null(result.ScheduleJob);
    }

    [Fact]
    public void ParseFile_DateNotInDdMmYyyyFormat_RejectsTheLine()
    {
        var line = ScheduleJobLine.Replace("18/09/2026", "2026-09-18");

        var result = SchImportService.ParseFile(new[] { line });

        Assert.Null(result.ScheduleJob);
    }

    [Fact]
    public void ParseFile_MalformedLine_IsIgnoredRatherThanThrowing()
    {
        var result = SchImportService.ParseFile(new[] { "not;enough;fields" });

        Assert.Null(result.ScheduleJob);
        Assert.Empty(result.ScheduleEntries);
    }

    [Fact]
    public void ParseFile_BlankLines_AreSkipped()
    {
        var result = SchImportService.ParseFile(new[] { "", "   ", ScheduleJobLine, "" });

        Assert.NotNull(result.ScheduleJob);
    }

    // ---- strictness: a file the parser can't fully understand is reported, never half-imported ----

    private static string[] GoodFile => new[] { ScheduleJobLine, ScheduleEntryLine };

    [Fact]
    public void ParseFile_WellFormedFile_HasNoProblems()
    {
        Assert.Empty(SchImportService.ParseFile(GoodFile).Problems);
    }

    [Fact]
    public void ParseFile_LineWithAnUnexpectedFieldCount_IsReportedWithItsLineNumber()
    {
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine, ScheduleEntryLine, "a;b;c;d;e" });

        var problem = Assert.Single(result.Problems);
        Assert.Contains("line 3", problem);
        Assert.Contains("5 fields", problem);
    }

    [Fact]
    public void ParseFile_MemberLineWithABadEqA_IsReportedRatherThanSilentlyDropped()
    {
        var bad = ScheduleEntryLine.Replace("89.12", "eighty-nine");

        var result = SchImportService.ParseFile(new[] { ScheduleJobLine, ScheduleEntryLine.Replace("E17", "I31"), bad });

        Assert.Contains(result.Problems, p => p.Contains("line 3") && p.Contains("EqA") && p.Contains("eighty-nine"));
    }

    [Fact]
    public void ParseFile_DuplicateMemberIds_AreReported()
    {
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine, ScheduleEntryLine, ScheduleEntryLine });

        Assert.Contains(result.Problems, p => p.Contains("'E17'") && p.Contains("2 times"));
    }

    [Fact]
    public void ParseFile_MembersOfADifferentJob_AreReported()
    {
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine, ScheduleEntryLine.Replace("59778MWF", "11111XYZ") });

        Assert.Contains(result.Problems, p => p.Contains("11111XYZ"));
    }

    [Fact]
    public void ParseFile_SecondScheduleJobRecord_IsReported()
    {
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine, ScheduleJobLine, ScheduleEntryLine });

        Assert.Contains(result.Problems, p => p.Contains("second ScheduleJob"));
    }

    [Fact]
    public void ParseFile_JobWithNoMembers_IsReported()
    {
        Assert.Contains(SchImportService.ParseFile(new[] { ScheduleJobLine }).Problems, p => p.Contains("no ScheduleEntry"));
    }

    [Fact]
    public void ParseFile_BadDateAndBadDesignStatus_ReportEveryProblemAtOnce()
    {
        var line = ScheduleJobLine.Replace("18/09/2026;18/09/2026", "2026-09-18;18/09/2026").Replace("FinalDesign", "Maybe");

        var result = SchImportService.ParseFile(new[] { line, ScheduleEntryLine });

        Assert.Contains(result.Problems, p => p.Contains("field 5") && p.Contains("dd/MM/yyyy"));
        Assert.Contains(result.Problems, p => p.Contains("field 16") && p.Contains("Maybe"));
    }
}
