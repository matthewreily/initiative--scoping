using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FiscalPeriodsAndCapitalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FiscalYearStartMonth",
                table: "WorkCalendarSettings",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Capitalization",
                table: "InitiativeNonLaborCosts",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Capitalization",
                table: "InitiativeAllocations",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Capitalization",
                table: "ForecastBaselineNonLaborLines",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Capitalization",
                table: "ForecastBaselineLines",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "Capitalization",
                table: "CostCatalogItems",
                type: "integer",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FiscalYearStartMonth",
                table: "WorkCalendarSettings");

            migrationBuilder.DropColumn(
                name: "Capitalization",
                table: "InitiativeNonLaborCosts");

            migrationBuilder.DropColumn(
                name: "Capitalization",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "Capitalization",
                table: "ForecastBaselineNonLaborLines");

            migrationBuilder.DropColumn(
                name: "Capitalization",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "Capitalization",
                table: "CostCatalogItems");
        }
    }
}
