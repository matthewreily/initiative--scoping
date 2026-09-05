using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixedDurationPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PlanningMode",
                table: "Initiatives",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateOnly>(
                name: "TargetEnd",
                table: "Initiatives",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "AllocationPercent",
                table: "InitiativeAllocations",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Holidays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Holidays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkCalendarSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    HoursPerDay = table.Column<decimal>(type: "numeric(4,2)", precision: 4, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCalendarSettings", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Holidays_Date",
                table: "Holidays",
                column: "Date",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Holidays");

            migrationBuilder.DropTable(
                name: "WorkCalendarSettings");

            migrationBuilder.DropColumn(
                name: "PlanningMode",
                table: "Initiatives");

            migrationBuilder.DropColumn(
                name: "TargetEnd",
                table: "Initiatives");

            migrationBuilder.DropColumn(
                name: "AllocationPercent",
                table: "InitiativeAllocations");
        }
    }
}
