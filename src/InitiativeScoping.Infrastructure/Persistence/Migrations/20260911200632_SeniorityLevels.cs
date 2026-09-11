using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeniorityLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Seniority",
                table: "RateCardEntries",
                newName: "SeniorityId");

            migrationBuilder.RenameIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_Seniority_Locatio~",
                table: "RateCardEntries",
                newName: "IX_RateCardEntries_RateCardId_ResourceTypeId_SeniorityId_Locat~");

            migrationBuilder.RenameColumn(
                name: "Seniority",
                table: "People",
                newName: "SeniorityId");

            migrationBuilder.RenameColumn(
                name: "Seniority",
                table: "InitiativeAllocations",
                newName: "SeniorityId");

            migrationBuilder.RenameColumn(
                name: "Seniority",
                table: "ForecastBaselineLines",
                newName: "SeniorityId");

            migrationBuilder.RenameColumn(
                name: "Seniority",
                table: "AllocationTemplateLines",
                newName: "SeniorityId");

            migrationBuilder.CreateTable(
                name: "SeniorityLevels",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SeniorityLevels", x => x.Id);
                });

            // Existing rows hold the retired Seniority enum values (Associate=1 .. Principal=5); seed the catalog with matching ids.
            migrationBuilder.Sql("""
                INSERT INTO "SeniorityLevels" ("Id", "Name", "SortOrder", "IsActive") VALUES
                    (1, 'Associate', 1, TRUE),
                    (2, 'Mid', 2, TRUE),
                    (3, 'Senior', 3, TRUE),
                    (4, 'Staff', 4, TRUE),
                    (5, 'Principal', 5, TRUE);
                SELECT setval(pg_get_serial_sequence('"SeniorityLevels"', 'Id'), 5);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_SeniorityId",
                table: "RateCardEntries",
                column: "SeniorityId");

            migrationBuilder.CreateIndex(
                name: "IX_People_SeniorityId",
                table: "People",
                column: "SeniorityId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_SeniorityId",
                table: "InitiativeAllocations",
                column: "SeniorityId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationTemplateLines_SeniorityId",
                table: "AllocationTemplateLines",
                column: "SeniorityId");

            migrationBuilder.CreateIndex(
                name: "IX_SeniorityLevels_Name",
                table: "SeniorityLevels",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_AllocationTemplateLines_SeniorityLevels_SeniorityId",
                table: "AllocationTemplateLines",
                column: "SeniorityId",
                principalTable: "SeniorityLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_InitiativeAllocations_SeniorityLevels_SeniorityId",
                table: "InitiativeAllocations",
                column: "SeniorityId",
                principalTable: "SeniorityLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_People_SeniorityLevels_SeniorityId",
                table: "People",
                column: "SeniorityId",
                principalTable: "SeniorityLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RateCardEntries_SeniorityLevels_SeniorityId",
                table: "RateCardEntries",
                column: "SeniorityId",
                principalTable: "SeniorityLevels",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AllocationTemplateLines_SeniorityLevels_SeniorityId",
                table: "AllocationTemplateLines");

            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_SeniorityLevels_SeniorityId",
                table: "InitiativeAllocations");

            migrationBuilder.DropForeignKey(
                name: "FK_People_SeniorityLevels_SeniorityId",
                table: "People");

            migrationBuilder.DropForeignKey(
                name: "FK_RateCardEntries_SeniorityLevels_SeniorityId",
                table: "RateCardEntries");

            migrationBuilder.DropTable(
                name: "SeniorityLevels");

            migrationBuilder.DropIndex(
                name: "IX_RateCardEntries_SeniorityId",
                table: "RateCardEntries");

            migrationBuilder.DropIndex(
                name: "IX_People_SeniorityId",
                table: "People");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_SeniorityId",
                table: "InitiativeAllocations");

            migrationBuilder.DropIndex(
                name: "IX_AllocationTemplateLines_SeniorityId",
                table: "AllocationTemplateLines");

            migrationBuilder.RenameColumn(
                name: "SeniorityId",
                table: "RateCardEntries",
                newName: "Seniority");

            migrationBuilder.RenameIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_SeniorityId_Locat~",
                table: "RateCardEntries",
                newName: "IX_RateCardEntries_RateCardId_ResourceTypeId_Seniority_Locatio~");

            migrationBuilder.RenameColumn(
                name: "SeniorityId",
                table: "People",
                newName: "Seniority");

            migrationBuilder.RenameColumn(
                name: "SeniorityId",
                table: "InitiativeAllocations",
                newName: "Seniority");

            migrationBuilder.RenameColumn(
                name: "SeniorityId",
                table: "ForecastBaselineLines",
                newName: "Seniority");

            migrationBuilder.RenameColumn(
                name: "SeniorityId",
                table: "AllocationTemplateLines",
                newName: "Seniority");
        }
    }
}
