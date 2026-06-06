using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DividendAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DividendAliases",
                columns: table => new
                {
                    NormalizedAlias = table.Column<string>(type: "TEXT", nullable: false),
                    Alias = table.Column<string>(type: "TEXT", nullable: false),
                    Isin = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DividendAliases", x => x.NormalizedAlias);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DividendAliases");
        }
    }
}
