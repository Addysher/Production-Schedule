using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RenameLinearLengthToEqaComponents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LinearLengthM",
                table: "JobMembers");

            migrationBuilder.AddColumn<decimal>(
                name: "ProcessEqA",
                table: "JobMembers",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SetupEqA",
                table: "JobMembers",
                type: "decimal(10,4)",
                precision: 10,
                scale: 4,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ProcessEqA",
                table: "JobMembers");

            migrationBuilder.DropColumn(
                name: "SetupEqA",
                table: "JobMembers");

            migrationBuilder.AddColumn<decimal>(
                name: "LinearLengthM",
                table: "JobMembers",
                type: "decimal(8,3)",
                precision: 8,
                scale: 3,
                nullable: true);
        }
    }
}
