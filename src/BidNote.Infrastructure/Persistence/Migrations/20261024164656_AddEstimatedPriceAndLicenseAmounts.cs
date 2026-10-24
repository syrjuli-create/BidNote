using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEstimatedPriceAndLicenseAmounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "EstimatedPrice",
                table: "Notices",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "CapacityAmount",
                table: "CompanyLicenses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PerformanceAmount",
                table: "CompanyLicenses",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PerformanceMultiple",
                table: "CompanyLicenses",
                type: "REAL",
                nullable: true);

            // 이미 받은 공고의 추정가격을 마지막 공고 목록 원문({notice, licenses})에서 채운다. 빈 값·0은 null.
            migrationBuilder.Sql("""
                UPDATE Notices SET EstimatedPrice = (
                    SELECT NULLIF(CAST(json_extract(r.ResponseJson, '$.notice.presmptPrce') AS INTEGER), 0)
                    FROM ApiRawResponses r
                    WHERE r.SourceSystem = Notices.SourceSystem AND r.BusinessType = Notices.BusinessType
                      AND r.BidNo = Notices.BidNo AND r.ApiName = 'getBidPblancListInfoCnstwk'
                    ORDER BY r.Id DESC LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EstimatedPrice",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "CapacityAmount",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "PerformanceAmount",
                table: "CompanyLicenses");

            migrationBuilder.DropColumn(
                name: "PerformanceMultiple",
                table: "CompanyLicenses");
        }
    }
}
