using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DevelopmentPods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PodId",
                table: "InitiativeAllocations",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationPlatforms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "case_insensitive"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationPlatforms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Pods",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false, collation: "case_insensitive"),
                    BusinessUnitId = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pods_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InitiativeApplications",
                columns: table => new
                {
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    ApplicationPlatformId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeApplications", x => new { x.InitiativeId, x.ApplicationPlatformId });
                    table.ForeignKey(
                        name: "FK_InitiativeApplications_ApplicationPlatforms_ApplicationPlat~",
                        column: x => x.ApplicationPlatformId,
                        principalTable: "ApplicationPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitiativeApplications_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodApplications",
                columns: table => new
                {
                    PodId = table.Column<int>(type: "integer", nullable: false),
                    ApplicationPlatformId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodApplications", x => new { x.PodId, x.ApplicationPlatformId });
                    table.ForeignKey(
                        name: "FK_PodApplications_ApplicationPlatforms_ApplicationPlatformId",
                        column: x => x.ApplicationPlatformId,
                        principalTable: "ApplicationPlatforms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PodApplications_Pods_PodId",
                        column: x => x.PodId,
                        principalTable: "Pods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PodMembers",
                columns: table => new
                {
                    PodId = table.Column<int>(type: "integer", nullable: false),
                    PersonId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PodMembers", x => new { x.PodId, x.PersonId });
                    table.ForeignKey(
                        name: "FK_PodMembers_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PodMembers_Pods_PodId",
                        column: x => x.PodId,
                        principalTable: "Pods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_PodId",
                table: "InitiativeAllocations",
                column: "PodId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationPlatforms_Name",
                table: "ApplicationPlatforms",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeApplications_ApplicationPlatformId",
                table: "InitiativeApplications",
                column: "ApplicationPlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_PodApplications_ApplicationPlatformId",
                table: "PodApplications",
                column: "ApplicationPlatformId");

            migrationBuilder.CreateIndex(
                name: "IX_PodMembers_PersonId",
                table: "PodMembers",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_Pods_BusinessUnitId",
                table: "Pods",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Pods_Name",
                table: "Pods",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InitiativeAllocations_Pods_PodId",
                table: "InitiativeAllocations",
                column: "PodId",
                principalTable: "Pods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_InitiativeAllocations_Pods_PodId",
                table: "InitiativeAllocations");

            migrationBuilder.DropTable(
                name: "InitiativeApplications");

            migrationBuilder.DropTable(
                name: "PodApplications");

            migrationBuilder.DropTable(
                name: "PodMembers");

            migrationBuilder.DropTable(
                name: "ApplicationPlatforms");

            migrationBuilder.DropTable(
                name: "Pods");

            migrationBuilder.DropIndex(
                name: "IX_InitiativeAllocations_PodId",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PodId",
                table: "InitiativeAllocations");
        }
    }
}
