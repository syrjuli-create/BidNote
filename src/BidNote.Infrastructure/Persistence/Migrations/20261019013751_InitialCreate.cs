using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApiRawResponses",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceSystem = table.Column<string>(type: "TEXT", nullable: false),
                    ApiName = table.Column<string>(type: "TEXT", nullable: false),
                    RequestParameter = table.Column<string>(type: "TEXT", nullable: false),
                    BusinessType = table.Column<int>(type: "INTEGER", nullable: false),
                    BidNo = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    CollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ParsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ParseVersion = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRawResponses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiUsages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ApiName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCalledAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiUsages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Notices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SourceSystem = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BusinessType = table.Column<int>(type: "INTEGER", nullable: false),
                    BidNo = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    BidSeq = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Agency = table.Column<string>(type: "TEXT", nullable: false),
                    DemandAgency = table.Column<string>(type: "TEXT", nullable: false),
                    Region = table.Column<string>(type: "TEXT", nullable: false),
                    License = table.Column<string>(type: "TEXT", nullable: false),
                    BasePrice = table.Column<long>(type: "INTEGER", nullable: true),
                    LowerLimitRate = table.Column<double>(type: "REAL", nullable: false),
                    AValue = table.Column<long>(type: "INTEGER", nullable: true),
                    NetCost = table.Column<long>(type: "INTEGER", nullable: true),
                    PostedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DueAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OpenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRebid = table.Column<bool>(type: "INTEGER", nullable: false),
                    CorrectionCount = table.Column<int>(type: "INTEGER", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstCollectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RawHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NoticeHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    BidSeq = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<int>(type: "INTEGER", nullable: false),
                    SnapshotJson = table.Column<string>(type: "TEXT", nullable: false),
                    RawHash = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeHistories_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiRawResponses_CollectedAt",
                table: "ApiRawResponses",
                column: "CollectedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ApiRawResponses_SourceSystem_BusinessType_BidNo",
                table: "ApiRawResponses",
                columns: new[] { "SourceSystem", "BusinessType", "BidNo" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiUsages_Date_ApiName",
                table: "ApiUsages",
                columns: new[] { "Date", "ApiName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NoticeHistories_NoticeId_DetectedAt",
                table: "NoticeHistories",
                columns: new[] { "NoticeId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_DueAt",
                table: "Notices",
                column: "DueAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_PostedAt",
                table: "Notices",
                column: "PostedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_SourceSystem_BusinessType_BidNo",
                table: "Notices",
                columns: new[] { "SourceSystem", "BusinessType", "BidNo" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiRawResponses");

            migrationBuilder.DropTable(
                name: "ApiUsages");

            migrationBuilder.DropTable(
                name: "NoticeHistories");

            migrationBuilder.DropTable(
                name: "Notices");
        }
    }
}
