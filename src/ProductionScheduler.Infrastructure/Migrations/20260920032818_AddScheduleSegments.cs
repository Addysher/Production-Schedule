using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleSegments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScheduleSegments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ScheduleItemId = table.Column<int>(type: "int", nullable: false),
                    SegmentDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SegmentStart = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SegmentEnd = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PlannedEqA = table.Column<decimal>(type: "decimal(10,4)", precision: 10, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScheduleSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScheduleSegments_ScheduleItems_ScheduleItemId",
                        column: x => x.ScheduleItemId,
                        principalTable: "ScheduleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSegments_Date",
                table: "ScheduleSegments",
                column: "SegmentDate");

            migrationBuilder.CreateIndex(
                name: "IX_ScheduleSegments_ScheduleItemId",
                table: "ScheduleSegments",
                column: "ScheduleItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScheduleSegments");
        }
    }
}
