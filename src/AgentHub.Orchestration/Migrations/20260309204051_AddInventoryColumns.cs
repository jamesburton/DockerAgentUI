using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Orchestration.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DispatchId",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParentSessionId",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InventoryJson",
                table: "Hosts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sessions_ParentSessionId",
                table: "Sessions",
                column: "ParentSessionId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sessions_Sessions_ParentSessionId",
                table: "Sessions",
                column: "ParentSessionId",
                principalTable: "Sessions",
                principalColumn: "SessionId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sessions_Sessions_ParentSessionId",
                table: "Sessions");

            migrationBuilder.DropIndex(
                name: "IX_Sessions_ParentSessionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DispatchId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "ParentSessionId",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "InventoryJson",
                table: "Hosts");
        }
    }
}
