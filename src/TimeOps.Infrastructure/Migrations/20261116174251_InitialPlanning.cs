using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TimeOps.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialPlanning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppUser",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Login = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsAdmin = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppUser", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuditEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    Entity = table.Column<string>(type: "text", nullable: false),
                    BeforeJson = table.Column<string>(type: "text", nullable: false),
                    AfterJson = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanningTeam",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: false),
                    WorkingDaysMask = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningTeam", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WebhookDelivery",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IssueNodeId = table.Column<string>(type: "text", nullable: true),
                    ProjectItemId = table.Column<string>(type: "text", nullable: true),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Processed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WebhookDelivery", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PlanningCycle",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningCycle", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningCycle_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningLeave",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", nullable: true),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningLeave", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningLeave_AppUser_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanningLeave_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningProduct",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Repository = table.Column<string>(type: "text", nullable: false),
                    RepositoryNodeId = table.Column<string>(type: "text", nullable: false),
                    ProjectNodeId = table.Column<string>(type: "text", nullable: false),
                    ProjectNumber = table.Column<int>(type: "integer", nullable: false),
                    ProjectUrl = table.Column<string>(type: "text", nullable: false),
                    StatusFieldId = table.Column<string>(type: "text", nullable: false),
                    StatusMappingJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningProduct", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningProduct_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningTask",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    RequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningTask", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningTask_AppUser_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanningTask_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamMembership",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", nullable: false),
                    Access = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamMembership", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamMembership_AppUser_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TeamMembership_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TeamRole",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamRole", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamRole_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningSprint",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    CycleId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Start = table.Column<DateOnly>(type: "date", nullable: false),
                    End = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningSprint", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningSprint_PlanningCycle_CycleId",
                        column: x => x.CycleId,
                        principalTable: "PlanningCycle",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlanningSprint_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CapacityAllocation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    TeamId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    SprintId = table.Column<int>(type: "integer", nullable: true),
                    HoursPerDay = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CapacityAllocation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CapacityAllocation_AppUser_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapacityAllocation_PlanningSprint_SprintId",
                        column: x => x.SprintId,
                        principalTable: "PlanningSprint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapacityAllocation_PlanningTeam_TeamId",
                        column: x => x.TeamId,
                        principalTable: "PlanningTeam",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CapacityAllocation_TeamRole_RoleId",
                        column: x => x.RoleId,
                        principalTable: "TeamRole",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Implementation",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    TaskId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    SprintId = table.Column<int>(type: "integer", nullable: true),
                    Original = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Remaining = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    IssueNodeId = table.Column<string>(type: "text", nullable: true),
                    IssueNumber = table.Column<int>(type: "integer", nullable: true),
                    IssueUrl = table.Column<string>(type: "text", nullable: true),
                    ProjectItemId = table.Column<string>(type: "text", nullable: true),
                    GitHubTitle = table.Column<string>(type: "text", nullable: true),
                    AssigneeIdsJson = table.Column<string>(type: "text", nullable: false),
                    StatusName = table.Column<string>(type: "text", nullable: false),
                    StateCategory = table.Column<string>(type: "text", nullable: false),
                    Archived = table.Column<bool>(type: "boolean", nullable: false),
                    PublicationState = table.Column<int>(type: "integer", nullable: false),
                    SyncedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SyncError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Implementation", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Implementation_PlanningProduct_ProductId",
                        column: x => x.ProductId,
                        principalTable: "PlanningProduct",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Implementation_PlanningSprint_SprintId",
                        column: x => x.SprintId,
                        principalTable: "PlanningSprint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Implementation_PlanningTask_TaskId",
                        column: x => x.TaskId,
                        principalTable: "PlanningTask",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlanningEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ImplementationId = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    FromSprintId = table.Column<int>(type: "integer", nullable: true),
                    ToSprintId = table.Column<int>(type: "integer", nullable: true),
                    Before = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    After = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ActorId = table.Column<string>(type: "text", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlanningEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlanningEvent_Implementation_ImplementationId",
                        column: x => x.ImplementationId,
                        principalTable: "Implementation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PublicationJob",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ImplementationId = table.Column<int>(type: "integer", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Completed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicationJob", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PublicationJob_Implementation_ImplementationId",
                        column: x => x.ImplementationId,
                        principalTable: "Implementation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WorkLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    ImplementationId = table.Column<int>(type: "integer", nullable: false),
                    SprintId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "character varying(100)", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    Hours = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    Voided = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkLog_AppUser_UserId",
                        column: x => x.UserId,
                        principalTable: "AppUser",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkLog_Implementation_ImplementationId",
                        column: x => x.ImplementationId,
                        principalTable: "Implementation",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WorkLog_PlanningSprint_SprintId",
                        column: x => x.SprintId,
                        principalTable: "PlanningSprint",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CapacityAllocation_RoleId",
                table: "CapacityAllocation",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityAllocation_SprintId",
                table: "CapacityAllocation",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_CapacityAllocation_TeamId_UserId_RoleId_SprintId",
                table: "CapacityAllocation",
                columns: new[] { "TeamId", "UserId", "RoleId", "SprintId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CapacityAllocation_UserId",
                table: "CapacityAllocation",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Implementation_CorrelationId",
                table: "Implementation",
                column: "CorrelationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Implementation_IssueNodeId",
                table: "Implementation",
                column: "IssueNodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Implementation_ProductId",
                table: "Implementation",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Implementation_SprintId",
                table: "Implementation",
                column: "SprintId");

            migrationBuilder.CreateIndex(
                name: "IX_Implementation_TaskId_ProductId",
                table: "Implementation",
                columns: new[] { "TaskId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningCycle_TeamId",
                table: "PlanningCycle",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningEvent_ImplementationId_At",
                table: "PlanningEvent",
                columns: new[] { "ImplementationId", "At" });

            migrationBuilder.CreateIndex(
                name: "IX_PlanningLeave_TeamId",
                table: "PlanningLeave",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningLeave_UserId",
                table: "PlanningLeave",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningProduct_TeamId",
                table: "PlanningProduct",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningSprint_CycleId",
                table: "PlanningSprint",
                column: "CycleId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningSprint_TeamId",
                table: "PlanningSprint",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningTask_CreatedBy",
                table: "PlanningTask",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_PlanningTask_RequestId",
                table: "PlanningTask",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlanningTask_TeamId",
                table: "PlanningTask",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJob_Completed_NextAttemptAt",
                table: "PublicationJob",
                columns: new[] { "Completed", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PublicationJob_ImplementationId",
                table: "PublicationJob",
                column: "ImplementationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembership_TeamId_UserId",
                table: "TeamMembership",
                columns: new[] { "TeamId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TeamMembership_UserId",
                table: "TeamMembership",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamRole_TeamId_Name",
                table: "TeamRole",
                columns: new[] { "TeamId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkLog_ImplementationId",
                table: "WorkLog",
                column: "ImplementationId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkLog_SprintId_UserId_Date",
                table: "WorkLog",
                columns: new[] { "SprintId", "UserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkLog_UserId",
                table: "WorkLog",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEntry");

            migrationBuilder.DropTable(
                name: "CapacityAllocation");

            migrationBuilder.DropTable(
                name: "PlanningEvent");

            migrationBuilder.DropTable(
                name: "PlanningLeave");

            migrationBuilder.DropTable(
                name: "PublicationJob");

            migrationBuilder.DropTable(
                name: "TeamMembership");

            migrationBuilder.DropTable(
                name: "WebhookDelivery");

            migrationBuilder.DropTable(
                name: "WorkLog");

            migrationBuilder.DropTable(
                name: "TeamRole");

            migrationBuilder.DropTable(
                name: "Implementation");

            migrationBuilder.DropTable(
                name: "PlanningProduct");

            migrationBuilder.DropTable(
                name: "PlanningSprint");

            migrationBuilder.DropTable(
                name: "PlanningTask");

            migrationBuilder.DropTable(
                name: "PlanningCycle");

            migrationBuilder.DropTable(
                name: "AppUser");

            migrationBuilder.DropTable(
                name: "PlanningTeam");
        }
    }
}
