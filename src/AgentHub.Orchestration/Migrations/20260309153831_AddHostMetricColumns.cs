using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentHub.Orchestration.Migrations
{
    /// <inheritdoc />
    public partial class AddHostMetricColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "CpuPercent",
                table: "Hosts",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MemTotalMb",
                table: "Hosts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "MemUsedMb",
                table: "Hosts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "MetricsUpdatedUtc",
                table: "Hosts",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CpuPercent",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "MemTotalMb",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "MemUsedMb",
                table: "Hosts");

            migrationBuilder.DropColumn(
                name: "MetricsUpdatedUtc",
                table: "Hosts");
        }
    }
}
