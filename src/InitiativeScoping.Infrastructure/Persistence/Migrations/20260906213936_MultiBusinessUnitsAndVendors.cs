using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiBusinessUnitsAndVendors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries");

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "RateCardEntries",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "People",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessUnitId",
                table: "InitiativeAllocations",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "InitiativeAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BusinessUnitId",
                table: "ForecastBaselineLines",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "ForecastBaselineLines",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InitiativeBusinessUnits",
                columns: table => new
                {
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    BusinessUnitId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeBusinessUnits", x => new { x.InitiativeId, x.BusinessUnitId });
                    table.ForeignKey(
                        name: "FK_InitiativeBusinessUnits_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitiativeBusinessUnits_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vendors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "case_insensitive"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendors", x => x.Id);
                });

            migrationBuilder.Sql("""
                UPDATE "InitiativeAllocations" a
                SET "BusinessUnitId" = i."BusinessUnitId"
                FROM "Initiatives" i
                WHERE i."Id" = a."InitiativeId";

                UPDATE "ForecastBaselineLines" l
                SET "BusinessUnitId" = i."BusinessUnitId"
                FROM "ForecastBaselines" b
                JOIN "Initiatives" i ON i."Id" = b."InitiativeId"
                WHERE b."Id" = l."ForecastBaselineId";
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries",
                columns: new[] { "RateCardId", "ResourceTypeId", "BusinessUnitId", "Seniority", "Location", "ResourcingClass", "VendorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_VendorId",
                table: "RateCardEntries",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_People_VendorId",
                table: "People",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_BusinessUnitId",
                table: "InitiativeAllocations",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_VendorId",
                table: "InitiativeAllocations",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeBusinessUnits_BusinessUnitId",
                table: "InitiativeBusinessUnits",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendors_Name",
                table: "Vendors",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InitiativeAllocations_BusinessUnits_BusinessUnitId",
                table: "InitiativeAllocations",
                column: "BusinessUnitId",
                principalTable: "BusinessUnits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InitiativeAllocations_Vendors_VendorId",
                table: "InitiativeAllocations",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_People_Vendors_VendorId",
                table: "People",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RateCardEntries_Vendors_VendorId",
                table: "RateCardEntries",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_BusinessUnits_BusinessUnitId",
                table: "InitiativeAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_Vendors_VendorId",
                table: "InitiativeAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_People_Vendors_VendorId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_RateCardEntries_Vendors_VendorId",
                table: "RateCardEntries");

            migrationBuilder.DropTable(
                name: "InitiativeBusinessUnits");

            migrationBuilder.DropTable(
                name: "Vendors");

            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries");

            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_VendorId",
                table: "RateCardEntries");

            migrationBuilder.DropIndex(
                name: "IX_People_VendorId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_BusinessUnitId",
                table: "InitiativeAllocations");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_VendorId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "RateCardEntries");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "People");

            migrationBuilder.DropColumn(
                name: "BusinessUnitId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "BusinessUnitId",
                table: "ForecastBaselineLines");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "ForecastBaselineLines");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries",
                columns: new[] { "RateCardId", "ResourceTypeId", "BusinessUnitId", "Seniority", "Location", "ResourcingClass" },
                unique: true);
        }
    }
}
