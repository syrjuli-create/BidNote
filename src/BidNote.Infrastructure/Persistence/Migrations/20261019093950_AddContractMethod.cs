using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContractMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContractMethod",
                table: "Notices",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            // 이미 받은 공고는 저장해 둔 가장 최근 목록 원문에서 계약방법을 채운다. "제한경쟁(1등급)"의 등급은 뗀다.
            migrationBuilder.Sql("""
                UPDATE Notices SET ContractMethod = COALESCE((
                    SELECT CASE WHEN m LIKE '%(%' THEN TRIM(substr(m, 1, instr(m, '(') - 1)) ELSE TRIM(m) END
                    FROM (SELECT json_extract(r.ResponseJson, '$.notice.cntrctCnclsMthdNm') AS m
                          FROM ApiRawResponses r
                          WHERE r.SourceSystem = Notices.SourceSystem AND r.BusinessType = Notices.BusinessType
                            AND r.BidNo = Notices.BidNo AND r.ApiName = 'getBidPblancListInfoCnstwk'
                            AND json_valid(r.ResponseJson)
                          ORDER BY r.Id DESC LIMIT 1)), '');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContractMethod",
                table: "Notices");
        }
    }
}
