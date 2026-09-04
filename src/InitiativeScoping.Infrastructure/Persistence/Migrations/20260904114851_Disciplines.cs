using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Disciplines : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Disciplines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false, collation: "case_insensitive"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Disciplines", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Disciplines_Name",
                table: "Disciplines",
                column: "Name",
                unique: true);

            // Backfill: one discipline per distinct legacy free-text value (case-insensitive), then link each resource type.
            migrationBuilder.Sql("""
                INSERT INTO "Disciplines" ("Name", "IsActive")
                SELECT MIN(TRIM("Discipline")), TRUE
                FROM "ResourceTypes"
                WHERE TRIM("Discipline") <> ''
                GROUP BY LOWER(TRIM("Discipline"));
                """);

            migrationBuilder.AddColumn<int>(
                name: "DisciplineId",
                table: "ResourceTypes",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE "ResourceTypes" rt
                SET "DisciplineId" = d."Id"
                FROM "Disciplines" d
                WHERE LOWER(TRIM(rt."Discipline")) = LOWER(d."Name");
                """);

            migrationBuilder.Sql("""
                INSERT INTO "Disciplines" ("Name", "IsActive")
                SELECT 'Unassigned', TRUE
                WHERE EXISTS (SELECT 1 FROM "ResourceTypes" WHERE "DisciplineId" IS NULL)
                  AND NOT EXISTS (SELECT 1 FROM "Disciplines" WHERE "Name" = 'Unassigned');

                UPDATE "ResourceTypes"
                SET "DisciplineId" = (SELECT "Id" FROM "Disciplines" WHERE "Name" = 'Unassigned')
                WHERE "DisciplineId" IS NULL;
                """);

            migrationBuilder.AlterColumn<int>(
                name: "DisciplineId",
                table: "ResourceTypes",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.DropColumn(
                name: "Discipline",
                table: "ResourceTypes");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypes_DisciplineId",
                table: "ResourceTypes",
                column: "DisciplineId");

            migrationBuilder.AddForeignKey(
                name: "FK_ResourceTypes_Disciplines_DisciplineId",
                table: "ResourceTypes",
                column: "DisciplineId",
                principalTable: "Disciplines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ResourceTypes_Disciplines_DisciplineId",
                table: "ResourceTypes");

            migrationBuilder.DropIndex(
                name: "IX_ResourceTypes_DisciplineId",
                table: "ResourceTypes");

            migrationBuilder.AddColumn<string>(
                name: "Discipline",
                table: "ResourceTypes",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE "ResourceTypes" rt
                SET "Discipline" = d."Name"
                FROM "Disciplines" d
                WHERE d."Id" = rt."DisciplineId";
                """);

            migrationBuilder.DropColumn(
                name: "DisciplineId",
                table: "ResourceTypes");

            migrationBuilder.DropTable(
                name: "Disciplines");
        }
    }
}
