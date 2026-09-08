using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class GlobalRateCardEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RateCardEntries_BusinessUnits_BusinessUnitId",
                table: "RateCardEntries");

            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_BusinessUnitId",
                table: "RateCardEntries");

            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries");

            // Rates become global: where several business units priced the same
            // (card, type, seniority, location, class, vendor) keep the highest rate.
            migrationBuilder.Sql("""
                DELETE FROM "RateCardEntries" e
                USING "RateCardEntries" o
                WHERE o."RateCardId" = e."RateCardId"
                  AND o."ResourceTypeId" = e."ResourceTypeId"
                  AND o."Seniority" = e."Seniority"
                  AND o."Location" = e."Location"
                  AND o."ResourcingClass" = e."ResourcingClass"
                  AND o."VendorId" IS NOT DISTINCT FROM e."VendorId"
                  AND o."Id" <> e."Id"
                  AND (o."HourlyRate" > e."HourlyRate"
                       OR (o."HourlyRate" = e."HourlyRate" AND o."Id" < e."Id"));
                """);

            migrationBuilder.DropColumn(
                name: "BusinessUnitId",
                table: "RateCardEntries");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_Seniority_Locatio~",
                table: "RateCardEntries",
                columns: new[] { "RateCardId", "ResourceTypeId", "Seniority", "Location", "ResourcingClass", "VendorId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_Seniority_Locatio~",
                table: "RateCardEntries");

            migrationBuilder.AddColumn<int>(
                name: "BusinessUnitId",
                table: "RateCardEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // The original business unit is gone; re-home every global entry on the
            // oldest business unit so the FK and unique index can be restored.
            migrationBuilder.Sql("""
                DELETE FROM "RateCardEntries" WHERE NOT EXISTS (SELECT 1 FROM "BusinessUnits");
                UPDATE "RateCardEntries" SET "BusinessUnitId" = (SELECT MIN("Id") FROM "BusinessUnits");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_BusinessUnitId",
                table: "RateCardEntries",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries",
                columns: new[] { "RateCardId", "ResourceTypeId", "BusinessUnitId", "Seniority", "Location", "ResourcingClass", "VendorId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_RateCardEntries_BusinessUnits_BusinessUnitId",
                table: "RateCardEntries",
                column: "BusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
