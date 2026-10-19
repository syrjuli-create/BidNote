using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLicensesAndFakeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DetailUrl",
                table: "Notices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SiteRegionName",
                table: "Notices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "NoticeLicenses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    NoticeId = table.Column<int>(type: "INTEGER", nullable: false),
                    GroupNo = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoticeLicenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoticeLicenses_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NoticeLicenses_Name",
                table: "NoticeLicenses",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_NoticeLicenses_NoticeId",
                table: "NoticeLicenses",
                column: "NoticeId");

            // 이전 가상 수집기는 출처를 G2B로 저장했다. 실제 공고와 구분해 지울 수 있도록 FAKE로 바꾼다.
            migrationBuilder.Sql("UPDATE Notices SET SourceSystem = 'FAKE' WHERE BidNo LIKE 'FAKE-%';");
            migrationBuilder.Sql("UPDATE ApiRawResponses SET SourceSystem = 'FAKE' WHERE BidNo LIKE 'FAKE-%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NoticeLicenses");

            migrationBuilder.DropColumn(
                name: "DetailUrl",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "SiteRegionName",
                table: "Notices");
        }
    }
}
