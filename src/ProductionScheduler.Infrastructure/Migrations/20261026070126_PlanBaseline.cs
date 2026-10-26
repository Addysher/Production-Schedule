using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PlanBaseline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlanBaselineSegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationId = table.Column<int>(type: "int", nullable: false),
                    JobRef = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Day = table.Column<DateOnly>(type: "date", nullable: false),
                    Start = table.Column<DateTime>(type: "datetime2", nullable: false),
                    End = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEqA = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanBaselineSegments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlanBaselineSegments_Day_Station",
                table: "PlanBaselineSegments",
                columns: new[] { "Day", "StationId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlanBaselineSegments");
        }
    }
}
