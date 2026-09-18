using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllocationPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PersonId",
                table: "InitiativeAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PersonId",
                table: "ForecastBaselineLines",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PersonName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_PersonId",
                table: "InitiativeAllocations",
                column: "PersonId");

            migrationBuilder.AddForeignKey(
                name: "FK_InitiativeAllocations_People_PersonId",
                table: "InitiativeAllocations",
                column: "PersonId",
                principalTable: "People",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_People_PersonId",
                table: "InitiativeAllocations");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_PersonId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "PersonName",
                table: "ForecastBaselineLines");
        }
    }
}
