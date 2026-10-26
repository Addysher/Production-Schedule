using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StationFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "UncPath",
                table: "Stations",
                newName: "PrdPath");

            migrationBuilder.AddColumn<string>(
                name: "MpsPath",
                table: "Stations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            // Until now one folder did both jobs, so existing stations keep sending MPS files where they
            // always went; set each station's real DataOut folder afterwards.
            migrationBuilder.Sql("UPDATE Stations SET MpsPath = PrdPath");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MpsPath",
                table: "Stations");

            migrationBuilder.RenameColumn(
                name: "PrdPath",
                table: "Stations",
                newName: "UncPath");
        }
    }
}
