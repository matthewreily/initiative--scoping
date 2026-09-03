using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace InitiativeScoping.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActualsImports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    StartedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    RecordCount = table.Column<int>(type: "integer", nullable: false),
                    UnmappedCount = table.Column<int>(type: "integer", nullable: false),
                    SkippedCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorCount = table.Column<int>(type: "integer", nullable: false),
                    Log = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualsImports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AllocationTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    SizeKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Entity = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DiffJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessUnits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessUnits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RateCards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EffectiveStart = table.Column<DateOnly>(type: "date", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateCards", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ResourceTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Discipline = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ResourceTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SizingConversions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Method = table.Column<int>(type: "integer", nullable: false),
                    Key = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SizingConversions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Initiatives",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    BusinessUnitId = table.Column<int>(type: "integer", nullable: false),
                    SponsoringTeam = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    SizingMethod = table.Column<int>(type: "integer", nullable: false),
                    SizeKey = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    TargetStart = table.Column<DateOnly>(type: "date", nullable: false),
                    VarianceThresholdPct = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Initiatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Initiatives_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "AllocationTemplateLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AllocationTemplateId = table.Column<int>(type: "integer", nullable: false),
                    PhaseName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ResourceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Seniority = table.Column<int>(type: "integer", nullable: false),
                    Percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllocationTemplateLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AllocationTemplateLines_AllocationTemplates_AllocationTempl~",
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

            migrationBuilder.CreateTable(
                name: "People",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalIds = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ResourceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Seniority = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourcingClass = table.Column<int>(type: "integer", nullable: false),
                    BusinessUnitId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_People", x => x.Id);
                    table.ForeignKey(
                        name: "FK_People_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_People_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RateCardEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RateCardId = table.Column<int>(type: "integer", nullable: false),
                    ResourceTypeId = table.Column<int>(type: "integer", nullable: false),
                    BusinessUnitId = table.Column<int>(type: "integer", nullable: false),
                    Seniority = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourcingClass = table.Column<int>(type: "integer", nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateCardEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RateCardEntries_BusinessUnits_BusinessUnitId",
                        column: x => x.BusinessUnitId,
                        principalTable: "BusinessUnits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RateCardEntries_RateCards_RateCardId",
                        column: x => x.RateCardId,
                        principalTable: "RateCards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RateCardEntries_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActualAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualAdjustments_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ForecastBaselines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    SnapshotAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SnapshotBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    TotalHours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastBaselines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastBaselines_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InitiativeMembers",
                columns: table => new
                {
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Role = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeMembers", x => new { x.InitiativeId, x.UserId });
                    table.ForeignKey(
                        name: "FK_InitiativeMembers_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InitiativeSourceMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExternalProjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeSourceMappings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InitiativeSourceMappings_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Phases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Phases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Phases_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ActualEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualsImportId = table.Column<int>(type: "integer", nullable: false),
                    InitiativeId = table.Column<int>(type: "integer", nullable: true),
                    PersonId = table.Column<int>(type: "integer", nullable: true),
                    ExternalProjectId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExternalPersonId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    WorkDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SourcedCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CalculatedCost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    SourceReference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsUnmapped = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualEntries_ActualsImports_ActualsImportId",
                        column: x => x.ActualsImportId,
                        principalTable: "ActualsImports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActualEntries_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualEntries_People_PersonId",
                        column: x => x.PersonId,
                        principalTable: "People",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ForecastBaselineLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ForecastBaselineId = table.Column<int>(type: "integer", nullable: false),
                    PhaseId = table.Column<int>(type: "integer", nullable: false),
                    ResourceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Seniority = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourcingClass = table.Column<int>(type: "integer", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    HourlyRate = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Cost = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForecastBaselineLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ForecastBaselineLines_ForecastBaselines_ForecastBaselineId",
                        column: x => x.ForecastBaselineId,
                        principalTable: "ForecastBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RebaselineRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    RequestedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DecidedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecisionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ResultingBaselineId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RebaselineRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RebaselineRequests_ForecastBaselines_ResultingBaselineId",
                        column: x => x.ResultingBaselineId,
                        principalTable: "ForecastBaselines",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RebaselineRequests_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "InitiativeAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    InitiativeId = table.Column<int>(type: "integer", nullable: false),
                    PhaseId = table.Column<int>(type: "integer", nullable: false),
                    ResourceTypeId = table.Column<int>(type: "integer", nullable: false),
                    Seniority = table.Column<int>(type: "integer", nullable: false),
                    Location = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ResourcingClass = table.Column<int>(type: "integer", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    EstimatedHours = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ContractReference = table.Column<string>(type: "text", nullable: true),
                    CostCenter = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InitiativeAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InitiativeAllocations_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InitiativeAllocations_Phases_PhaseId",
                        column: x => x.PhaseId,
                        principalTable: "Phases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InitiativeAllocations_ResourceTypes_ResourceTypeId",
                        column: x => x.ResourceTypeId,
                        principalTable: "ResourceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PhaseDateHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PhaseId = table.Column<int>(type: "integer", nullable: false),
                    OldStart = table.Column<DateOnly>(type: "date", nullable: false),
                    OldEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    NewStart = table.Column<DateOnly>(type: "date", nullable: false),
                    NewEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    ChangedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhaseDateHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhaseDateHistories_Phases_PhaseId",
                        column: x => x.PhaseId,
                        principalTable: "Phases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActualAdjustments_InitiativeId",
                table: "ActualAdjustments",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_ActualsImportId",
                table: "ActualEntries",
                column: "ActualsImportId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_InitiativeId_IsUnmapped_WorkDate",
                table: "ActualEntries",
                columns: new[] { "InitiativeId", "IsUnmapped", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_IsUnmapped",
                table: "ActualEntries",
                column: "IsUnmapped");

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_PersonId",
                table: "ActualEntries",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualEntries_SourceReference",
                table: "ActualEntries",
                column: "SourceReference");

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

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Action",
                table: "AuditEvents",
                column: "Action");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_At",
                table: "AuditEvents",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_Entity_EntityId",
                table: "AuditEvents",
                columns: new[] { "Entity", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessUnits_Name",
                table: "BusinessUnits",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForecastBaselineLines_ForecastBaselineId",
                table: "ForecastBaselineLines",
                column: "ForecastBaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_ForecastBaselines_InitiativeId_IsCurrent",
                table: "ForecastBaselines",
                columns: new[] { "InitiativeId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_ForecastBaselines_InitiativeId_Version",
                table: "ForecastBaselines",
                columns: new[] { "InitiativeId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_InitiativeId",
                table: "InitiativeAllocations",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_PhaseId",
                table: "InitiativeAllocations",
                column: "PhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeAllocations_ResourceTypeId",
                table: "InitiativeAllocations",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_BusinessUnitId",
                table: "Initiatives",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_Status",
                table: "Initiatives",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeSourceMappings_InitiativeId",
                table: "InitiativeSourceMappings",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_InitiativeSourceMappings_Source_ExternalProjectId",
                table: "InitiativeSourceMappings",
                columns: new[] { "Source", "ExternalProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_People_BusinessUnitId",
                table: "People",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_People_ResourceTypeId",
                table: "People",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_PhaseDateHistories_PhaseId",
                table: "PhaseDateHistories",
                column: "PhaseId");

            migrationBuilder.CreateIndex(
                name: "IX_Phases_InitiativeId",
                table: "Phases",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_BusinessUnitId",
                table: "RateCardEntries",
                column: "BusinessUnitId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_RateCardId_ResourceTypeId_BusinessUnitId_Se~",
                table: "RateCardEntries",
                columns: new[] { "RateCardId", "ResourceTypeId", "BusinessUnitId", "Seniority", "Location", "ResourcingClass" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RateCardEntries_ResourceTypeId",
                table: "RateCardEntries",
                column: "ResourceTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_Status_EffectiveStart",
                table: "RateCards",
                columns: new[] { "Status", "EffectiveStart" });

            migrationBuilder.CreateIndex(
                name: "IX_RebaselineRequests_InitiativeId_Status",
                table: "RebaselineRequests",
                columns: new[] { "InitiativeId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RebaselineRequests_ResultingBaselineId",
                table: "RebaselineRequests",
                column: "ResultingBaselineId");

            migrationBuilder.CreateIndex(
                name: "IX_ResourceTypes_Name",
                table: "ResourceTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SizingConversions_Method_Key",
                table: "SizingConversions",
                columns: new[] { "Method", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActualAdjustments");

            migrationBuilder.DropTable(
                name: "ActualEntries");

            migrationBuilder.DropTable(
                name: "AllocationTemplateLines");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "ForecastBaselineLines");

            migrationBuilder.DropTable(
                name: "InitiativeAllocations");

            migrationBuilder.DropTable(
                name: "InitiativeMembers");

            migrationBuilder.DropTable(
                name: "InitiativeSourceMappings");

            migrationBuilder.DropTable(
                name: "PhaseDateHistories");

            migrationBuilder.DropTable(
                name: "RateCardEntries");

            migrationBuilder.DropTable(
                name: "RebaselineRequests");

            migrationBuilder.DropTable(
                name: "SizingConversions");

            migrationBuilder.DropTable(
                name: "ActualsImports");

            migrationBuilder.DropTable(
                name: "People");

            migrationBuilder.DropTable(
                name: "AllocationTemplates");

            migrationBuilder.DropTable(
                name: "Phases");

            migrationBuilder.DropTable(
                name: "RateCards");

            migrationBuilder.DropTable(
                name: "ForecastBaselines");

            migrationBuilder.DropTable(
                name: "ResourceTypes");

            migrationBuilder.DropTable(
                name: "Initiatives");

            migrationBuilder.DropTable(
                name: "BusinessUnits");
        }
    }
}
