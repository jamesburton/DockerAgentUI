using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Orchestration.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hosts",
                columns: table => new
                {
                    HostId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", nullable: false),
                    Backend = table.Column<string>(type: "TEXT", nullable: false),
                    Os = table.Column<string>(type: "TEXT", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AllowSsh = table.Column<bool>(type: "INTEGER", nullable: false),
                    LabelsJson = table.Column<string>(type: "TEXT", nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    LastSeenUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hosts", x => x.HostId);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Backend = table.Column<string>(type: "TEXT", nullable: false),
                    Node = table.Column<string>(type: "TEXT", nullable: true),
                    AgentType = table.Column<string>(type: "TEXT", nullable: true),
                    RequirementsJson = table.Column<string>(type: "TEXT", nullable: false),
                    WorktreePath = table.Column<string>(type: "TEXT", nullable: true),
                    RiskAcceptedBy = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SessionId = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", nullable: false),
                    TsUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Data = table.Column<string>(type: "TEXT", nullable: false),
                    MetaJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Events_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "SessionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Events_SessionId_Id",
                table: "Events",
                columns: new[] { "SessionId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_OwnerUserId",
                table: "Sessions",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_State",
                table: "Sessions",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "Hosts");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
