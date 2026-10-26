using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProductsAndPoolFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SchDeletedAt",
                table: "Jobs");

            migrationBuilder.AddColumn<int>(
                name: "MpsStationId",
                table: "Jobs",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SchComponentType",
                table: "Jobs",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PoolPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ArchivePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_MpsStationId",
                table: "Jobs",
                column: "MpsStationId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Name",
                table: "Products",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Jobs_Stations_MpsStationId",
                table: "Jobs",
                column: "MpsStationId",
                principalTable: "Stations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Every component type already in use becomes a product, so existing stations, job types and
            // jobs stay matched. A product's pool folder comes from an SCH import path that was scoped to it.
            migrationBuilder.Sql("""
                INSERT INTO Products (Name, PoolPath, ArchivePath, IsActive)
                SELECT n.Name,
                       (SELECT TOP 1 p.UncPath FROM SchImportPaths p WHERE p.ComponentType = n.Name AND p.IsActive = 1),
                       NULL, 1
                FROM (
                    SELECT ComponentType AS Name FROM Stations
                    UNION SELECT ComponentType FROM JobTypes WHERE ComponentType IS NOT NULL
                    UNION SELECT ComponentType FROM Jobs WHERE ComponentType IS NOT NULL
                    UNION SELECT ComponentType FROM SchImportPaths WHERE ComponentType IS NOT NULL
                ) n
                WHERE n.Name <> '';
                """);

            migrationBuilder.Sql("UPDATE Jobs SET SchComponentType = ComponentType;");

            migrationBuilder.DropTable(
                name: "SchImportPaths");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Jobs_Stations_MpsStationId",
                table: "Jobs");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Jobs_MpsStationId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "MpsStationId",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "SchComponentType",
                table: "Jobs");

            migrationBuilder.AddColumn<DateTime>(
                name: "SchDeletedAt",
                table: "Jobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SchImportPaths",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ComponentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    UncPath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SchImportPaths", x => x.Id);
                });
        }
    }
}
