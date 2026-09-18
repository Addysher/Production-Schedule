using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BatchcutWatchPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UncPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchcutWatchPaths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MinsPerUnit = table.Column<decimal>(type: "decimal(8,2)", precision: 8, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MaintenanceLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TaskName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    JobRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SourcePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DestPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Outcome = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    RunAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MaintenanceLogs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SchImportPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UncPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchImportPaths", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "SetupTimes",
                columns: table => new
                {
                    FromComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ToComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DurationMins = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SetupTimes", x => new { x.FromComponentType, x.ToComponentType });
                });

            migrationBuilder.CreateTable(
                name: "Shifts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Shifts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Stations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UncPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DisplayOrder = table.Column<int>(type: "int", nullable: false),
                    DailyEqATarget = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    ArchivePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Stations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Username = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BatchcutEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchcutPathId = table.Column<int>(type: "int", nullable: false),
                    FolderName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    JobRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    DetectedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ErrorMessage = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BatchcutEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BatchcutEvents_BatchcutWatchPaths_BatchcutPathId",
                        column: x => x.BatchcutPathId,
                        principalTable: "BatchcutWatchPaths",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Operators",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ShiftId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Operators", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Operators_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ShiftBreaks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ShiftId = table.Column<int>(type: "int", nullable: false),
                    BreakStart = table.Column<TimeOnly>(type: "time", nullable: false),
                    BreakDurationMins = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShiftBreaks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShiftBreaks_Shifts_ShiftId",
                        column: x => x.ShiftId,
                        principalTable: "Shifts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PrdReadState",
                columns: table => new
                {
                    StationId = table.Column<int>(type: "int", nullable: false),
                    FileDate = table.Column<DateOnly>(type: "date", nullable: false),
                    LastLineRead = table.Column<int>(type: "int", nullable: false),
                    LastProcessedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrdReadState", x => new { x.StationId, x.FileDate });
                    table.ForeignKey(
                        name: "FK_PrdReadState_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StationCapacityOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<int>(type: "int", nullable: false),
                    OverrideDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EqATarget = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationCapacityOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationCapacityOverrides_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ClientName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ExpectedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EstBookedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClientRefNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SiteAddress = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    SiteSuburb = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    SupervisorName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SupervisorNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    JobOrderDate = table.Column<DateOnly>(type: "date", nullable: true),
                    JobOrderTime = table.Column<TimeOnly>(type: "time", nullable: true),
                    DesignStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Raw2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Raw7 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Raw8 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    AssignedStationId = table.Column<int>(type: "int", nullable: true),
                    EstimatedStartDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EstimatedCompletionDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    TotalEqA = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    CompletedEqA = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    ApprovedForCuttingAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    SchSourcePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MpsSourcePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsManuallyScheduled = table.Column<bool>(type: "bit", nullable: false),
                    ManualStationId = table.Column<int>(type: "int", nullable: true),
                    ManualScheduleDate = table.Column<DateOnly>(type: "date", nullable: true),
                    BookedDeliveryDate = table.Column<DateOnly>(type: "date", nullable: true),
                    InvoiceNumber = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    Delivered = table.Column<bool>(type: "bit", nullable: false),
                    DeliveredAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SchDeletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    MpsArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                    table.UniqueConstraint("AK_Jobs_JobRef", x => x.JobRef);
                    table.ForeignKey(
                        name: "FK_Jobs_Stations_AssignedStationId",
                        column: x => x.AssignedStationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Jobs_Stations_ManualStationId",
                        column: x => x.ManualStationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Jobs_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JobMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobRef = table.Column<string>(type: "nvarchar(50)", nullable: false),
                    MemberId = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    MemberCategory = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LinearLengthM = table.Column<decimal>(type: "decimal(8,3)", precision: 8, scale: 3, nullable: true),
                    WidthMm = table.Column<int>(type: "int", nullable: true),
                    HeightMm = table.Column<int>(type: "int", nullable: true),
                    Batch = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    SubSequence = table.Column<int>(type: "int", nullable: false),
                    EqaValue = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    Done = table.Column<bool>(type: "bit", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedEqA = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: true),
                    Raw2 = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobMembers_Jobs_JobRef",
                        column: x => x.JobRef,
                        principalTable: "Jobs",
                        principalColumn: "JobRef",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Precedences",
                columns: table => new
                {
                    PredecessorJobRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SuccessorJobRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Precedences", x => new { x.PredecessorJobRef, x.SuccessorJobRef });
                    table.ForeignKey(
                        name: "FK_Precedences_Jobs_PredecessorJobRef",
                        column: x => x.PredecessorJobRef,
                        principalTable: "Jobs",
                        principalColumn: "JobRef",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Precedences_Jobs_SuccessorJobRef",
                        column: x => x.SuccessorJobRef,
                        principalTable: "Jobs",
                        principalColumn: "JobRef",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ScheduleItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JobRef = table.Column<string>(type: "nvarchar(50)", nullable: false),
                    StationId = table.Column<int>(type: "int", nullable: false),
                    OperatorId = table.Column<int>(type: "int", nullable: true),
                    ScheduleDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEqA = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MembersTotal = table.Column<int>(type: "int", nullable: false),
                    ActualStart = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ActualEnd = table.Column<DateTime>(type: "datetime2", nullable: true),
                    EqaCompleted = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false),
                    MembersCompleted = table.Column<int>(type: "int", nullable: false),
                    CreepDetected = table.Column<bool>(type: "bit", nullable: false),
                    CreepDetectedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreepApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreepApprovedByUserId = table.Column<int>(type: "int", nullable: true),
                    GeneratedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsArchived = table.Column<bool>(type: "bit", nullable: false),
                    ArchivedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleItems_Jobs_JobRef",
                        column: x => x.JobRef,
                        principalTable: "Jobs",
                        principalColumn: "JobRef",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScheduleItems_Operators_OperatorId",
                        column: x => x.OperatorId,
                        principalTable: "Operators",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleItems_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ScheduleItems_Users_CreepApprovedByUserId",
                        column: x => x.CreepApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BatchcutEvents_BatchcutPathId",
                table: "BatchcutEvents",
                column: "BatchcutPathId");

            migrationBuilder.CreateIndex(
                name: "IX_BatchcutEvents_JobRef",
                table: "BatchcutEvents",
                columns: new[] { "JobRef", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_JobMembers_JobRef",
                table: "JobMembers",
                column: "JobRef");

            migrationBuilder.CreateIndex(
                name: "UQ_JobMembers_Ref",
                table: "JobMembers",
                columns: new[] { "JobRef", "MemberId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ApprovedByUserId",
                table: "Jobs",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_JobRef",
                table: "Jobs",
                column: "JobRef",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ManualSchedule",
                table: "Jobs",
                columns: new[] { "IsManuallyScheduled", "ManualScheduleDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_ManualStationId",
                table: "Jobs",
                column: "ManualStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Station",
                table: "Jobs",
                columns: new[] { "AssignedStationId", "DesignStatus", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_Status",
                table: "Jobs",
                columns: new[] { "Status", "DesignStatus", "IsArchived" });

            migrationBuilder.CreateIndex(
                name: "IX_Operators_ShiftId",
                table: "Operators",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_PrdReadState_Station_Date",
                table: "PrdReadState",
                columns: new[] { "StationId", "FileDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Precedences_SuccessorJobRef",
                table: "Precedences",
                column: "SuccessorJobRef");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_Creep",
                table: "ScheduleItems",
                columns: new[] { "CreepDetected", "CreepApprovedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_CreepApprovedByUserId",
                table: "ScheduleItems",
                column: "CreepApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_Date_Station",
                table: "ScheduleItems",
                columns: new[] { "ScheduleDate", "StationId" });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_OperatorId",
                table: "ScheduleItems",
                column: "OperatorId");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleItems_StationId",
                table: "ScheduleItems",
                column: "StationId");

            migrationBuilder.CreateIndex(
                name: "UQ_ScheduleItems_Job_Date",
                table: "ScheduleItems",
                columns: new[] { "JobRef", "ScheduleDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShiftBreaks_ShiftId",
                table: "ShiftBreaks",
                column: "ShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_StationCapacityOverrides_Station_Date",
                table: "StationCapacityOverrides",
                columns: new[] { "StationId", "OverrideDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Stations_ComponentType",
                table: "Stations",
                columns: new[] { "ComponentType", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_Users_Username",
                table: "Users",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BatchcutEvents");

            migrationBuilder.DropTable(
                name: "JobMembers");

            migrationBuilder.DropTable(
                name: "JobTypes");

            migrationBuilder.DropTable(
                name: "MaintenanceLogs");

            migrationBuilder.DropTable(
                name: "PrdReadState");

            migrationBuilder.DropTable(
                name: "Precedences");

            migrationBuilder.DropTable(
                name: "ScheduleItems");

            migrationBuilder.DropTable(
                name: "SchImportPaths");

            migrationBuilder.DropTable(
                name: "Settings");

            migrationBuilder.DropTable(
                name: "SetupTimes");

            migrationBuilder.DropTable(
                name: "ShiftBreaks");

            migrationBuilder.DropTable(
                name: "StationCapacityOverrides");

            migrationBuilder.DropTable(
                name: "BatchcutWatchPaths");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Operators");

            migrationBuilder.DropTable(
                name: "Stations");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "Shifts");
        }
    }
}
