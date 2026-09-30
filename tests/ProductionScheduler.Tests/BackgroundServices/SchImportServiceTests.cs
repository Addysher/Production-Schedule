using ProductionScheduler.Scheduling.BackgroundServices;
using Xunit;

namespace ProductionScheduler.Tests.BackgroundServices;

public class SchImportServiceTests
{
    // Field values taken from the §5.8 worked examples, but in the shape a real Sapphire export
    // actually uses (checked against real .sch files): each line carries a "ScheduleJob:" /
    // "ScheduleEntry:" record-type label, and fields are delimited with "; " — a space after the
    // semicolon — not just ";".
    private const string ScheduleJobLine =
        "ScheduleJob: 59778MWF; ?; WallFrames; Dale Alcock Homes; 18/09/2026; 18/09/2026; 0; 2; 38477; #62 Bathers Promenade; Jindalee; ; ; 02/09/2026; 14:23:12; FinalDesign";

    // Field 13 (SetupEqA) is 0.00 here, matching real WallFrames data — FramingMachine is a single
    // activity, so the SetupJig component that RoofTrusses/PosiTrusses jobs carry never applies.
    // Field 23 ("89.12" — the design doc's worked example puts EqA here) is left as an inert
    // placeholder: checked against three real jobs' known totals, it isn't EqA and isn't read.
    private const string ScheduleEntryLine =
        "ScheduleEntry: 59778MWF; ?; WallFrames; E17; x5; x6; x7; Ext Load Bearing; x9; x10; x11; x12; 0.00; 7.05; x15; x16; 4498; 2692; x19; x20; x21; False; 89.12; x24; 1; 1; 1";

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
        Assert.Equal(0.00m, entry.SetupEqA);
        Assert.Equal(7.05m, entry.ProcessEqA);
        Assert.Equal(4498, entry.WidthMm);
        Assert.Equal(2692, entry.HeightMm);
        Assert.False(entry.Done);
        Assert.Equal(7.05m, entry.EqaValue);
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
        var bad = ScheduleEntryLine.Replace("7.05", "eighty-nine");

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
        var line = ScheduleJobLine.Replace("18/09/2026; 18/09/2026", "2026-09-18; 18/09/2026").Replace("FinalDesign", "Maybe");

        var result = SchImportService.ParseFile(new[] { line, ScheduleEntryLine });

        Assert.Contains(result.Problems, p => p.Contains("field 5") && p.Contains("dd/MM/yyyy"));
        Assert.Contains(result.Problems, p => p.Contains("field 16") && p.Contains("Maybe"));
    }

    // ---- real Sapphire export shape: record-type labels, header lines, "; "-delimited fields ----

    [Theory]
    [InlineData("File Version= 1.04")]
    [InlineData("Production Units Names= EqA")]
    [InlineData("Batchcut: False; ITSWS-AD15; C:\\MiTek\\Sapphire\\programs\\Mibat.exe; ; ")]
    [InlineData("Activities: ProcessJig; SetupJig")]
    [InlineData("Activities: FramingMachine")]
    public void ParseFile_SapphireHeaderLines_AreSkippedNotReported(string headerLine)
    {
        var result = SchImportService.ParseFile(new[] { headerLine, ScheduleJobLine, ScheduleEntryLine });

        Assert.Empty(result.Problems);
        Assert.NotNull(result.ScheduleJob);
    }

    [Fact]
    public void ParseFile_UnlabelledLine_IsReportedRatherThanMistakenForARecord()
    {
        // No file this parser has ever seen omits the "ScheduleJob:"/"ScheduleEntry:" label — a line
        // without one (and that isn't a known header) is unrecognised, not silently field-counted.
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine.Replace("ScheduleJob: ", ""), ScheduleEntryLine });

        Assert.Contains(result.Problems, p => p.Contains("line 1") && p.Contains("unrecognised line"));
    }

    [Fact]
    public void ParseFile_RealWorldSpacing_TrimsLeadingSpaceFromStringFields()
    {
        // "; " (space after the semicolon) is how Sapphire actually delimits fields — the fields that
        // don't go through Field()'s .Trim() (Raw2, ClientName, MemberCategory, ...) must still come
        // out clean rather than carrying that leading space into the database.
        var result = SchImportService.ParseFile(new[] { ScheduleJobLine, ScheduleEntryLine });

        Assert.Equal("?", result.ScheduleJob!.Raw2);
        Assert.Equal("Dale Alcock Homes", result.ScheduleJob!.ClientName);
        Assert.Equal("#62 Bathers Promenade", result.ScheduleJob!.SiteAddress);
        Assert.Equal("Ext Load Bearing", Assert.Single(result.ScheduleEntries).MemberCategory);
    }

    // ---- the three real .sch files supplied for this job (real_data/, copied to RealSchFiles/ on build) ----

    private static string[] RealFile(string name) => File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "RealSchFiles", name));

    [Fact]
    public void ParseFile_RealFloorTrussFile_58749MPF_ParsesCleanly()
    {
        var result = SchImportService.ParseFile(RealFile("58749MPF__.sch"));

        Assert.Empty(result.Problems);
        Assert.NotNull(result.ScheduleJob);
        Assert.Equal("58749MPF", result.ScheduleJob!.JobRef);
        Assert.Equal("PosiTrusses", result.ScheduleJob.ComponentType);
        Assert.Equal("FinalDesign", result.ScheduleJob.DesignStatus);
        Assert.Equal(12, result.ScheduleEntries.Count);
        // 129.19 is the job's real, independently-known total EqA — confirmed by the user, and exactly
        // reproduced by field 13 + field 14 (field 13/SetupEqA is 0 throughout this file).
        Assert.Equal(129.19m, result.ScheduleEntries.Sum(e => e.EqaValue));
        Assert.All(result.ScheduleEntries, e => Assert.Equal("58749MPF", e.JobRef));
    }

    [Fact]
    public void ParseFile_RealWallFrameFile_60497MWG_ParsesCleanly()
    {
        var result = SchImportService.ParseFile(RealFile("60497MWG__.sch"));

        Assert.Empty(result.Problems);
        Assert.NotNull(result.ScheduleJob);
        Assert.Equal("60497MWG", result.ScheduleJob!.JobRef);
        Assert.Equal("WallFrames", result.ScheduleJob.ComponentType);
        Assert.Equal(11, result.ScheduleEntries.Count);
        // The user recalled this job's total as "38" (rounded); 38.63 is the exact sum of field 14
        // (field 13/SetupEqA is 0 throughout — WallFrames' FramingMachine is a single activity).
        Assert.Equal(38.63m, result.ScheduleEntries.Sum(e => e.EqaValue));
        // Real WallFrames entries carry batch/sequence numbers (bundling for the framing machine),
        // unlike the roof/floor truss files where every entry is unbatched (0;0;0).
        Assert.Contains(result.ScheduleEntries, e => e.Batch > 0);
    }

    [Fact]
    public void ParseFile_RealRoofTrussFile_60656MTG_ParsesCleanly()
    {
        var result = SchImportService.ParseFile(RealFile("60656MTG__-RT.SCH"));

        Assert.Empty(result.Problems);
        Assert.NotNull(result.ScheduleJob);
        Assert.Equal("60656MTG", result.ScheduleJob!.JobRef);
        Assert.Equal("RoofTrusses", result.ScheduleJob.ComponentType);
        Assert.Equal(22, result.ScheduleEntries.Count);
        // 158.11 is the job's real total EqA, confirmed by the user — unlike the other two files, this
        // one only reconciles once field 13 (SetupJig, non-zero here) is added to field 14 (ProcessJig):
        // field 14 alone sums to 32.11, field 13 alone to 126.00.
        Assert.Equal(158.11m, result.ScheduleEntries.Sum(e => e.EqaValue));
        Assert.Equal(126.00m, result.ScheduleEntries.Sum(e => e.SetupEqA));
        Assert.Equal(32.11m, result.ScheduleEntries.Sum(e => e.ProcessEqA));
    }
}
