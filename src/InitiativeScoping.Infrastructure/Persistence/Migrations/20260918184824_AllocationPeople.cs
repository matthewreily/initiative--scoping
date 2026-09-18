using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AllocationPeople : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InitiativeAllocationPeople",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllocationId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeAllocationPeople", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InitiativeAllocationPeople_InitiativeAllocations_Allocation~",
                        column: x => x.AllocationId,
                        principalTable: "InitiativeAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InitiativeAllocationPeople_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocationPeople_AllocationId_PersonId",
                table: "InitiativeAllocationPeople",
                columns: new[] { "AllocationId", "PersonId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocationPeople_PersonId",
                table: "InitiativeAllocationPeople",
                column: "PersonId");

            // Each existing single-person allocation becomes one named seat.
            migrationBuilder.Sql("""
                INSERT INTO "InitiativeAllocationPeople" ("AllocationId", "PersonId")
                SELECT "Id", "PersonId" FROM "InitiativeAllocations" WHERE "PersonId" IS NOT NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_People_PersonId",
                table: "InitiativeAllocations");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_PersonId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PersonId",
                table: "InitiativeAllocations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PersonId",
                table: "InitiativeAllocations",
                type: "integer",
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

            // Keep the lowest-id named seat per allocation; the rest cannot be represented by a single column.
            migrationBuilder.Sql("""
                UPDATE "InitiativeAllocations" a SET "PersonId" = s."PersonId"
                FROM (SELECT DISTINCT ON ("AllocationId") "AllocationId", "PersonId" FROM "InitiativeAllocationPeople" ORDER BY "AllocationId", "Id") s
                WHERE s."AllocationId" = a."Id";
                """);

            migrationBuilder.DropTable(
                name: "InitiativeAllocationPeople");
        }
    }
}
