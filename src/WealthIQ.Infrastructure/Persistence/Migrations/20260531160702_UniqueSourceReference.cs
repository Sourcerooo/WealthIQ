using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UniqueSourceReference : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioEntries_SourceSystem_SourceRecordReference",
                table: "PortfolioEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioEntries_SourceSystem_SourceRecordReference",
                table: "PortfolioEntries",
                columns: new[] { "SourceSystem", "SourceRecordReference" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PortfolioEntries_SourceSystem_SourceRecordReference",
                table: "PortfolioEntries");

            migrationBuilder.CreateIndex(
                name: "IX_PortfolioEntries_SourceSystem_SourceRecordReference",
                table: "PortfolioEntries",
                columns: new[] { "SourceSystem", "SourceRecordReference" });
        }
    }
}
