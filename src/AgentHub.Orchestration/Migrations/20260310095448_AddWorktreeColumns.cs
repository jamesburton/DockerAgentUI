using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Orchestration.Migrations
{
    /// <inheritdoc />
    public partial class AddWorktreeColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KeepBranch",
                table: "Sessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WorktreeBranch",
                table: "Sessions",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KeepBranch",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "WorktreeBranch",
                table: "Sessions");
        }
    }
}
