using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BidNote.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMainFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MainFields",
                table: "NoticeLicenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MainFields",
                table: "CompanyLicenses",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MainFields",
                table: "NoticeLicenses");

            migrationBuilder.DropColumn(
                name: "MainFields",
                table: "CompanyLicenses");
        }
    }
}
