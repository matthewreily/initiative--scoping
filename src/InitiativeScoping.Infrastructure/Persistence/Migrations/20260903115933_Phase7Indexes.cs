using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Indexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RebaselineRequests_InitiativeId",
                table: "RebaselineRequests");

            migrationBuilder.DropIndex(
                name: "IX_ActualEntries_InitiativeId",
                table: "ActualEntries");

            migrationBuilder.CreateIndex(
                name: "IX_RebaselineRequests_InitiativeId_Status",
                table: "RebaselineRequests",
                columns: new[] { "InitiativeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_Status_EffectiveStart",
                table: "RateCards",
                columns: new[] { "Status", "EffectiveStart" });

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_Status",
                table: "Initiatives",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_ForecastBaselines_InitiativeId_IsCurrent",
                table: "ForecastBaselines",
                columns: new[] { "InitiativeId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action",
                table: "AuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_At",
                table: "AuditEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_InitiativeId_IsUnmapped_WorkDate",
                table: "ActualEntries",
                columns: new[] { "InitiativeId", "IsUnmapped", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_IsUnmapped",
                table: "ActualEntries",
                column: "IsUnmapped");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RebaselineRequests_InitiativeId_Status",
                table: "RebaselineRequests");

            migrationBuilder.DropIndex(
                name: "IX_RateCards_Status_EffectiveStart",
                table: "RateCards");

            migrationBuilder.DropIndex(
                name: "IX_Initiatives_Status",
                table: "Initiatives");

            migrationBuilder.DropIndex(
                name: "IX_ForecastBaselines_InitiativeId_IsCurrent",
                table: "ForecastBaselines");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_Action",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_At",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_ActualEntries_InitiativeId_IsUnmapped_WorkDate",
                table: "ActualEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActualEntries_IsUnmapped",
                table: "ActualEntries");

            migrationBuilder.CreateIndex(
                name: "IX_RebaselineRequests_InitiativeId",
                table: "RebaselineRequests",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_InitiativeId",
                table: "ActualEntries",
                column: "InitiativeId");
        }
    }
}
