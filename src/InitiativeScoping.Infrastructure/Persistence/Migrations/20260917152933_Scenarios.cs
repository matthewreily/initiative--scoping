using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Scenarios : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScenarioOfId",
                table: "Initiatives",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_ScenarioOfId",
                table: "Initiatives",
                column: "ScenarioOfId");

            migrationBuilder.AddForeignKey(
                name: "FK_Initiatives_Initiatives_ScenarioOfId",
                table: "Initiatives",
                column: "ScenarioOfId",
                principalTable: "Initiatives",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Initiatives_Initiatives_ScenarioOfId",
                table: "Initiatives");

            migrationBuilder.DropIndex(
                name: "IX_Initiatives_ScenarioOfId",
                table: "Initiatives");

            migrationBuilder.DropColumn(
                name: "ScenarioOfId",
                table: "Initiatives");
        }
    }
}
