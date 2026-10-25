using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRegionLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RegionLimitHash",
                table: "Notices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "RegionsLastSuccessTo",
                table: "CollectorSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyLocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsHeadOffice = table.Column<bool>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyLocations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoticeRegionLimits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeRegionLimits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeRegionLimits_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyLocations_Name",
                table: "CompanyLocations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeRegionLimits_NoticeId",
                table: "NoticeRegionLimits",
                column: "NoticeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyLocations");

            migrationBuilder.DropTable(
                name: "NoticeRegionLimits");

            migrationBuilder.DropColumn(
                name: "RegionLimitHash",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "RegionsLastSuccessTo",
                table: "CollectorSettings");
        }
    }
}
