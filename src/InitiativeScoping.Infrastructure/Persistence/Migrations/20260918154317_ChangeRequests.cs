using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChangeRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    EstimatedCostImpact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    EstimatedHoursImpact = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ProposedTargetEnd = table.Column<DateOnly>(type: "date", nullable: true),
                    BaselineVersionBefore = table.Column<int>(type: "integer", nullable: true),
                    ForecastHoursBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ForecastCostBefore = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TargetEndBefore = table.Column<DateOnly>(type: "date", nullable: true),
                    ForecastHoursAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ForecastCostAfter = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    TargetEndAfter = table.Column<DateOnly>(type: "date", nullable: true),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    RebaselineRequestId = table.Column<int>(type: "integer", nullable: true),
                    ResultingBaselineId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_ForecastBaselines_ResultingBaselineId",
                        column: x => x.ResultingBaselineId,
                        principalTable: "ForecastBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChangeRequests_RebaselineRequests_RebaselineRequestId",
                        column: x => x.RebaselineRequestId,
                        principalTable: "RebaselineRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_InitiativeId_Number",
                table: "ChangeRequests",
                columns: new[] { "InitiativeId", "Number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_InitiativeId_Status",
                table: "ChangeRequests",
                columns: new[] { "InitiativeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_RebaselineRequestId",
                table: "ChangeRequests",
                column: "RebaselineRequestId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeRequests_ResultingBaselineId",
                table: "ChangeRequests",
                column: "ResultingBaselineId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeRequests");
        }
    }
}
