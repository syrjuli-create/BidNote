using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBidResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BidResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    BidSeq = table.Column<string>(type: "TEXT", nullable: false),
                    ClassNo = table.Column<string>(type: "TEXT", nullable: false),
                    RebidNo = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    ParticipantCount = table.Column<int>(type: "INTEGER", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    WinnerName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    WinnerBizNo = table.Column<string>(type: "TEXT", nullable: false),
                    WinnerAmount = table.Column<long>(type: "INTEGER", nullable: true),
                    WinnerBidRate = table.Column<double>(type: "REAL", nullable: true),
                    PlannedPrice = table.Column<long>(type: "INTEGER", nullable: true),
                    ResultBasePrice = table.Column<long>(type: "INTEGER", nullable: true),
                    RanksFetchedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    RanksComplete = table.Column<bool>(type: "INTEGER", nullable: false),
                    RawHash = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BidResults_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BidResultRanks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    BidResultId = table.Column<int>(type: "INTEGER", nullable: false),
                    Rank = table.Column<int>(type: "INTEGER", nullable: true),
                    CompanyName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    BizNo = table.Column<string>(type: "TEXT", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: true),
                    BidRate = table.Column<double>(type: "REAL", nullable: true),
                    Remark = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BidResultRanks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BidResultRanks_BidResults_BidResultId",
                        column: x => x.BidResultId,
                        principalTable: "BidResults",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BidResultRanks_BidResultId_Rank",
                table: "BidResultRanks",
                columns: new[] { "BidResultId", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_BidResults_NoticeId",
                table: "BidResults",
                column: "NoticeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BidResultRanks");

            migrationBuilder.DropTable(
                name: "BidResults");
        }
    }
}
