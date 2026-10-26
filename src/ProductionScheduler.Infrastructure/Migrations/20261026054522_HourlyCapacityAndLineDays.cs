using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HourlyCapacityAndLineDays : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "DailyEqATarget",
                table: "Stations",
                newName: "EqaPerHour");

            // A line's speed used to be its product's job type "mins/unit" (minutes per EqA); keep exactly that
            // speed as EqA per hour. A line with no job type but a daily target gets target ÷ 10 hours.
            migrationBuilder.Sql(@"
UPDATE s SET EqaPerHour =
    CASE WHEN jt.MinsPerUnit > 0 THEN 60.0 / jt.MinsPerUnit
         WHEN s.EqaPerHour IS NOT NULL THEN s.EqaPerHour / 10.0
         ELSE NULL END
FROM Stations s
OUTER APPLY (SELECT TOP 1 MinsPerUnit FROM JobTypes WHERE ComponentType = s.ComponentType AND IsActive = 1 ORDER BY Id) jt;");

            // Old overrides were an EqA target for the day; they can't become working hours, so they go.
            migrationBuilder.Sql("DELETE FROM StationCapacityOverrides;");

            migrationBuilder.DropColumn(
                name: "EqATarget",
                table: "StationCapacityOverrides");

            migrationBuilder.DropColumn(
                name: "MinsPerUnit",
                table: "JobTypes");

            migrationBuilder.AddColumn<TimeOnly>(
                name: "EndTime",
                table: "StationCapacityOverrides",
                type: "time",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsClosed",
                table: "StationCapacityOverrides",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Note",
                table: "StationCapacityOverrides",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<TimeOnly>(
                name: "StartTime",
                table: "StationCapacityOverrides",
                type: "time",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "StationCapacityOverrides");

            migrationBuilder.DropColumn(
                name: "IsClosed",
                table: "StationCapacityOverrides");

            migrationBuilder.DropColumn(
                name: "Note",
                table: "StationCapacityOverrides");

            migrationBuilder.DropColumn(
                name: "StartTime",
                table: "StationCapacityOverrides");

            migrationBuilder.RenameColumn(
                name: "EqaPerHour",
                table: "Stations",
                newName: "DailyEqATarget");

            migrationBuilder.AddColumn<decimal>(
                name: "EqATarget",
                table: "StationCapacityOverrides",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "MinsPerUnit",
                table: "JobTypes",
                type: "decimal(8,2)",
                precision: 8,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
