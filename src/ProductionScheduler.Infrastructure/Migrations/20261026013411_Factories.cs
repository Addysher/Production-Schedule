using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Factories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FactoryId",
                table: "Stations",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "SawBundleStatus",
                table: "Jobs",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "Factories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Factories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Stations_FactoryId",
                table: "Stations",
                column: "FactoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Factories_Name",
                table: "Factories",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Stations_Factories_FactoryId",
                table: "Stations",
                column: "FactoryId",
                principalTable: "Factories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Stations_Factories_FactoryId",
                table: "Stations");

            migrationBuilder.DropTable(
                name: "Factories");

            migrationBuilder.DropIndex(
                name: "IX_Stations_FactoryId",
                table: "Stations");

            migrationBuilder.DropColumn(
                name: "FactoryId",
                table: "Stations");

            migrationBuilder.AlterColumn<string>(
                name: "SawBundleStatus",
                table: "Jobs",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20,
                oldNullable: true);
        }
    }
}
