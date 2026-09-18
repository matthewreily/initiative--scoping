using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ResourcingClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ResourcingClass",
                table: "RateCardEntries",
                newName: "ResourcingClassId");

            migrationBuilder.RenameColumn(
                name: "ResourcingClass",
                table: "People",
                newName: "ResourcingClassId");

            migrationBuilder.RenameColumn(
                name: "ResourcingClass",
                table: "InitiativeAllocations",
                newName: "ResourcingClassId");

            migrationBuilder.RenameColumn(
                name: "ResourcingClass",
                table: "ForecastBaselineLines",
                newName: "ResourcingClassId");

            migrationBuilder.AddColumn<string>(
                name: "ResourcingClassName",
                table: "ForecastBaselineLines",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ResourcingClasses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    IsVendor = table.Column<bool>(type: "boolean", nullable: false),
                    DefaultCapexPercent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourcingClasses", x => x.Id);
                });

            // Existing rows hold the retired ResourcingClass enum values (InternalFte=1, Vendor=2); seed the catalog with matching ids,
            // carrying the labor Capex % defaults over from WorkCalendarSettings (70 / 100 when no settings row exists yet).
            migrationBuilder.Sql("""
                INSERT INTO "ResourcingClasses" ("Id", "Name", "IsVendor", "DefaultCapexPercent", "SortOrder", "IsActive") VALUES
                    (1, 'Internal', FALSE, COALESCE((SELECT "InternalCapexPercent" FROM "WorkCalendarSettings" ORDER BY "Id" LIMIT 1), 70), 1, TRUE),
                    (2, 'Vendor', TRUE, COALESCE((SELECT "VendorCapexPercent" FROM "WorkCalendarSettings" ORDER BY "Id" LIMIT 1), 100), 2, TRUE);
                SELECT setval(pg_get_serial_sequence('"ResourcingClasses"', 'Id'), 2);
                UPDATE "ForecastBaselineLines" SET "ResourcingClassName" = CASE "ResourcingClassId" WHEN 2 THEN 'Vendor' ELSE 'Internal' END;
                """);

            migrationBuilder.DropColumn(
                name: "InternalCapexPercent",
                table: "WorkCalendarSettings");

            migrationBuilder.DropColumn(
                name: "VendorCapexPercent",
                table: "WorkCalendarSettings");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_ResourcingClassId",
                table: "RateCardEntries",
                column: "ResourcingClassId");

            migrationBuilder.CreateIndex(
                name: "IX_People_ResourcingClassId",
                table: "People",
                column: "ResourcingClassId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_ResourcingClassId",
                table: "InitiativeAllocations",
                column: "ResourcingClassId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourcingClasses_Name",
                table: "ResourcingClasses",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InitiativeAllocations_ResourcingClasses_ResourcingClassId",
                table: "InitiativeAllocations",
                column: "ResourcingClassId",
                principalTable: "ResourcingClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_People_ResourcingClasses_ResourcingClassId",
                table: "People",
                column: "ResourcingClassId",
                principalTable: "ResourcingClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RateCardEntries_ResourcingClasses_ResourcingClassId",
                table: "RateCardEntries",
                column: "ResourcingClassId",
                principalTable: "ResourcingClasses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_ResourcingClasses_ResourcingClassId",
                table: "InitiativeAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_People_ResourcingClasses_ResourcingClassId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_RateCardEntries_ResourcingClasses_ResourcingClassId",
                table: "RateCardEntries");


            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_ResourcingClassId",
                table: "RateCardEntries");

            migrationBuilder.DropIndex(
                name: "IX_People_ResourcingClassId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_ResourcingClassId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "ResourcingClassName",
                table: "ForecastBaselineLines");

            migrationBuilder.RenameColumn(
                name: "ResourcingClassId",
                table: "RateCardEntries",
                newName: "ResourcingClass");

            migrationBuilder.RenameColumn(
                name: "ResourcingClassId",
                table: "People",
                newName: "ResourcingClass");

            migrationBuilder.RenameColumn(
                name: "ResourcingClassId",
                table: "InitiativeAllocations",
                newName: "ResourcingClass");

            migrationBuilder.RenameColumn(
                name: "ResourcingClassId",
                table: "ForecastBaselineLines",
                newName: "ResourcingClass");

            migrationBuilder.AddColumn<decimal>(
                name: "InternalCapexPercent",
                table: "WorkCalendarSettings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VendorCapexPercent",
                table: "WorkCalendarSettings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.Sql("""
                UPDATE "WorkCalendarSettings" SET
                    "InternalCapexPercent" = COALESCE((SELECT "DefaultCapexPercent" FROM "ResourcingClasses" WHERE "Id" = 1), 70),
                    "VendorCapexPercent" = COALESCE((SELECT "DefaultCapexPercent" FROM "ResourcingClasses" WHERE "Id" = 2), 100);
                """);

            migrationBuilder.DropTable(
                name: "ResourcingClasses");
        }
    }
}
