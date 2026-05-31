using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ImportBatchStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "ImportBatches",
                type: "TEXT",
                nullable: false,
                defaultValue: "Committed");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "ImportBatches");
        }
    }
}
