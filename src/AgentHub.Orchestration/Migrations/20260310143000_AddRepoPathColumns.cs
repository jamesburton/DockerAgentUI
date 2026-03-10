using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Orchestration.Migrations
{
    /// <inheritdoc />
    public partial class AddRepoPathColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RepoPath",
                table: "Sessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultRepoPath",
                table: "Hosts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RepoPath",
                table: "Sessions");

            migrationBuilder.DropColumn(
                name: "DefaultRepoPath",
                table: "Hosts");
        }
    }
}
