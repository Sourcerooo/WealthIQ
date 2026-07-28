using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxAssetClass : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TaxAssetClass",
                table: "InstrumentProfiles",
                type: "TEXT",
                nullable: true);

            // Backfill the new classification from the free-text Type column. This is a one-time
            // data migration, not runtime inference: the fail-fast rule "explicit profile, no
            // derivation" still holds at report time for anything left NULL here.
            migrationBuilder.Sql(@"
                UPDATE InstrumentProfiles SET TaxAssetClass = 'equity_fund'   WHERE Type = 'ETF_EQUITY';
                UPDATE InstrumentProfiles SET TaxAssetClass = 'other_fund'    WHERE Type IN ('ETF_BOND', 'ETF_MONEY_MARKET');
                UPDATE InstrumentProfiles SET TaxAssetClass = 'share'         WHERE Type = 'STOCK';
            ");

            // An ETC is a secured debt security with limited recourse, not an investment fund.
            // The InvStG does not apply: no Vorabpauschale, no Teilfreistellung, and its gains are
            // declared on Anlage KAP Zeile 19 rather than KAP-INV. Profiles that claimed otherwise
            // (IE00B4ND3602) are corrected here. Deliberately NOT reverted in Down(): this is a
            // data correction, and re-introducing the wrong flag on a rollback would be worse than
            // leaving it right.
            migrationBuilder.Sql(@"
                UPDATE InstrumentProfiles
                   SET TaxAssetClass = 'other_security',
                       SubjectToVorabpauschale = 0
                 WHERE Type = 'ETC';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxAssetClass",
                table: "InstrumentProfiles");
        }
    }
}
