using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BaselineLineNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessUnitName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PhaseName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "ResourceTypeName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SeniorityName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "VendorName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            // Existing baselines predate name snapshots; seed them from the current catalog names.
            migrationBuilder.Sql("""
                UPDATE "ForecastBaselineLines" l SET
                    "PhaseName" = COALESCE((SELECT p."Name" FROM "Phases" p WHERE p."Id" = l."PhaseId"), 'Phase #' || l."PhaseId"),
                    "BusinessUnitName" = COALESCE((SELECT b."Name" FROM "BusinessUnits" b WHERE b."Id" = l."BusinessUnitId"), 'BU #' || l."BusinessUnitId"),
                    "ResourceTypeName" = COALESCE((SELECT t."Name" FROM "ResourceTypes" t WHERE t."Id" = l."ResourceTypeId"), 'Type #' || l."ResourceTypeId"),
                    "SeniorityName" = COALESCE((SELECT s."Name" FROM "SeniorityLevels" s WHERE s."Id" = l."SeniorityId"), 'Seniority #' || l."SeniorityId"),
                    "VendorName" = CASE WHEN l."VendorId" IS NULL THEN NULL
                        ELSE COALESCE((SELECT v."Name" FROM "Vendors" v WHERE v."Id" = l."VendorId"), 'Vendor #' || l."VendorId") END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessUnitName",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "PhaseName",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "ResourceTypeName",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "SeniorityName",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "VendorName",
                table: "ForecastBaselineLines");
        }
    }
}
