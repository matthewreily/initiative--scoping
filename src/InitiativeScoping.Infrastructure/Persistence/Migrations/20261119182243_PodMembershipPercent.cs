using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PodMembershipPercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "MembershipPercent",
                table: "PodMembers",
                type: "numeric",
                nullable: false,
                defaultValue: 100m);

            migrationBuilder.AddColumn<decimal>(
                name: "PodAllocationPercent",
                table: "InitiativeAllocations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PodMembershipPercent",
                table: "InitiativeAllocations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PodWrittenAllocationPercent",
                table: "InitiativeAllocations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PodWrittenEstimatedHours",
                table: "InitiativeAllocations",
                type: "numeric",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PodMembers_MembershipPercent",
                table: "PodMembers",
                sql: "CAST(\"MembershipPercent\" AS numeric) >= 1 AND CAST(\"MembershipPercent\" AS numeric) <= 100");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PodMembers_MembershipPercent",
                table: "PodMembers");

            migrationBuilder.DropColumn(
                name: "MembershipPercent",
                table: "PodMembers");

            migrationBuilder.DropColumn(
                name: "PodAllocationPercent",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PodMembershipPercent",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PodWrittenAllocationPercent",
                table: "InitiativeAllocations");

            migrationBuilder.DropColumn(
                name: "PodWrittenEstimatedHours",
                table: "InitiativeAllocations");
        }
    }
}
