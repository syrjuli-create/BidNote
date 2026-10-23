using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMyBids : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MyBids",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssessmentRate = table.Column<double>(type: "REAL", nullable: false),
                    BidAmount = table.Column<long>(type: "INTEGER", nullable: false),
                    BasePriceAtBid = table.Column<long>(type: "INTEGER", nullable: true),
                    AValueAtBid = table.Column<long>(type: "INTEGER", nullable: true),
                    Memo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MyBids", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MyBids_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MyBids_NoticeId",
                table: "MyBids",
                column: "NoticeId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MyBids");
        }
    }
}
