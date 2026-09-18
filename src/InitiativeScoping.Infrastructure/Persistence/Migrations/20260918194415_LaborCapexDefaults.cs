using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LaborCapexDefaults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "InternalCapexPercent",
                table: "WorkCalendarSettings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 70m);

            migrationBuilder.AddColumn<decimal>(
                name: "VendorCapexPercent",
                table: "WorkCalendarSettings",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 100m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InternalCapexPercent",
                table: "WorkCalendarSettings");

            migrationBuilder.DropColumn(
                name: "VendorCapexPercent",
                table: "WorkCalendarSettings");
        }
    }
}
