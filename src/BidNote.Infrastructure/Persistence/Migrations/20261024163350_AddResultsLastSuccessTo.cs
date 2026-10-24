using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddResultsLastSuccessTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ResultsLastSuccessTo",
                table: "CollectorSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ResultsLastSuccessTo",
                table: "CollectorSettings");
        }
    }
}
