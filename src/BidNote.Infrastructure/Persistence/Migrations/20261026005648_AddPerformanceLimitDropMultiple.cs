using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPerformanceLimitDropMultiple : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerformanceMultiple",
                table: "CompanyLicenses");

            migrationBuilder.AddColumn<DateTime>(
                name: "PerformanceDocDueAt",
                table: "Notices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PerformanceDocMethod",
                table: "Notices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "PerformanceLimited",
                table: "Notices",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // 이미 받은 공고는 저장해 둔 가장 최근 공고 목록 원문에서 실적제한 여부·서류 마감·접수 방법을 채운다.
            migrationBuilder.Sql("""
                UPDATE Notices SET
                    PerformanceLimited = COALESCE((SELECT json_extract(r.ResponseJson, '$.notice.arsltCmptYn') = 'Y' FROM ApiRawResponses r
                        WHERE r.SourceSystem = Notices.SourceSystem AND r.BusinessType = Notices.BusinessType AND r.BidNo = Notices.BidNo
                          AND r.ApiName = 'getBidPblancListInfoCnstwk' AND json_valid(r.ResponseJson) ORDER BY r.Id DESC LIMIT 1), 0),
                    PerformanceDocDueAt = (SELECT NULLIF(json_extract(r.ResponseJson, '$.notice.arsltApplDocRcptDt'), '') FROM ApiRawResponses r
                        WHERE r.SourceSystem = Notices.SourceSystem AND r.BusinessType = Notices.BusinessType AND r.BidNo = Notices.BidNo
                          AND r.ApiName = 'getBidPblancListInfoCnstwk' AND json_valid(r.ResponseJson) ORDER BY r.Id DESC LIMIT 1),
                    PerformanceDocMethod = COALESCE((SELECT json_extract(r.ResponseJson, '$.notice.arsltApplDocRcptMthdNm') FROM ApiRawResponses r
                        WHERE r.SourceSystem = Notices.SourceSystem AND r.BusinessType = Notices.BusinessType AND r.BidNo = Notices.BidNo
                          AND r.ApiName = 'getBidPblancListInfoCnstwk' AND json_valid(r.ResponseJson) ORDER BY r.Id DESC LIMIT 1), '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PerformanceDocDueAt",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "PerformanceDocMethod",
                table: "Notices");

            migrationBuilder.DropColumn(
                name: "PerformanceLimited",
                table: "Notices");

            migrationBuilder.AddColumn<double>(
                name: "PerformanceMultiple",
                table: "CompanyLicenses",
                type: "REAL",
                nullable: true);
        }
    }
}
