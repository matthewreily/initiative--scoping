using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase1AdminConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllocationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Method = table.Column<int>(type: "int", nullable: false),
                    SizeKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllocationTemplateLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AllocationTemplateId = table.Column<int>(type: "int", nullable: false),
                    PhaseName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    ResourceTypeId = table.Column<int>(type: "int", nullable: false),
                    Seniority = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationTemplateLines_AllocationTemplates_AllocationTemplateId",
                        column: x => x.AllocationTemplateId,
                        principalTable: "AllocationTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AllocationTemplateLines_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllocationTemplateLines_AllocationTemplateId",
                table: "AllocationTemplateLines",
                column: "AllocationTemplateId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationTemplateLines_ResourceTypeId",
                table: "AllocationTemplateLines",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_AllocationTemplates_Method_SizeKey",
                table: "AllocationTemplates",
                columns: new[] { "Method", "SizeKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllocationTemplateLines");

            migrationBuilder.DropTable(
                name: "AllocationTemplates");
        }
    }
}
