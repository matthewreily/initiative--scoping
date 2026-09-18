using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CapexPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "CapexPercent",
                table: "InitiativeNonLaborCosts",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CapexPercent",
                table: "InitiativeAllocations",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CapexPercent",
                table: "ForecastBaselineNonLaborLines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CapexPercent",
                table: "ForecastBaselineLines",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CapexPercent",
                table: "CostCatalogItems",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Opex (1) → 0 %, Capex (2) → 100 %.
            migrationBuilder.Sql("""UPDATE "InitiativeNonLaborCosts" SET "CapexPercent" = CASE WHEN "Capitalization" = 2 THEN 100 ELSE 0 END;""");
            migrationBuilder.Sql("""UPDATE "InitiativeAllocations" SET "CapexPercent" = CASE WHEN "Capitalization" = 2 THEN 100 ELSE 0 END;""");
            migrationBuilder.Sql("""UPDATE "ForecastBaselineNonLaborLines" SET "CapexPercent" = CASE WHEN "Capitalization" = 2 THEN 100 ELSE 0 END;""");
            migrationBuilder.Sql("""UPDATE "ForecastBaselineLines" SET "CapexPercent" = CASE WHEN "Capitalization" = 2 THEN 100 ELSE 0 END;""");
            migrationBuilder.Sql("""UPDATE "CostCatalogItems" SET "CapexPercent" = CASE WHEN "Capitalization" = 2 THEN 100 ELSE 0 END;""");

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

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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

            // Any capitalised share counts as Capex (2); otherwise Opex (1).
            migrationBuilder.Sql("""UPDATE "InitiativeNonLaborCosts" SET "Capitalization" = CASE WHEN "CapexPercent" > 0 THEN 2 ELSE 1 END;""");
            migrationBuilder.Sql("""UPDATE "InitiativeAllocations" SET "Capitalization" = CASE WHEN "CapexPercent" > 0 THEN 2 ELSE 1 END;""");
            migrationBuilder.Sql("""UPDATE "ForecastBaselineNonLaborLines" SET "Capitalization" = CASE WHEN "CapexPercent" > 0 THEN 2 ELSE 1 END;""");
            migrationBuilder.Sql("""UPDATE "ForecastBaselineLines" SET "Capitalization" = CASE WHEN "CapexPercent" > 0 THEN 2 ELSE 1 END;""");
            migrationBuilder.Sql("""UPDATE "CostCatalogItems" SET "Capitalization" = CASE WHEN "CapexPercent" > 0 THEN 2 ELSE 1 END;""");

            migrationBuilder.DropColumn(
                name: "CapexPercent",
                table: "InitiativeNonLaborCosts");

            migrationBuilder.DropColumn(
                name: "CapexPercent",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "CapexPercent",
                table: "ForecastBaselineNonLaborLines");

            migrationBuilder.DropColumn(
                name: "CapexPercent",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "CapexPercent",
                table: "CostCatalogItems");
        }
    }
}
