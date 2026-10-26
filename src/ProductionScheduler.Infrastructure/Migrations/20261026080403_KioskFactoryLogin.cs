using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionScheduler.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KioskFactoryLogin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FactoryId",
                table: "Users",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Users_FactoryId",
                table: "Users",
                column: "FactoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Factories_FactoryId",
                table: "Users",
                column: "FactoryId",
                principalTable: "Factories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Users_Factories_FactoryId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_FactoryId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "FactoryId",
                table: "Users");
        }
    }
}
