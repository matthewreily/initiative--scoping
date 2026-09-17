using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Contingency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ContingencyPct",
                table: "Initiatives",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "EstimateConfidence",
                table: "Initiatives",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ContingencyCost",
                table: "ForecastBaselines",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ContingencyPct",
                table: "ForecastBaselines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "EstimateConfidence",
                table: "ForecastBaselines",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContingencyPct",
                table: "Initiatives");

            migrationBuilder.DropColumn(
                name: "EstimateConfidence",
                table: "Initiatives");

            migrationBuilder.DropColumn(
                name: "ContingencyCost",
                table: "ForecastBaselines");

            migrationBuilder.DropColumn(
                name: "ContingencyPct",
                table: "ForecastBaselines");

            migrationBuilder.DropColumn(
                name: "EstimateConfidence",
                table: "ForecastBaselines");
        }
    }
}
