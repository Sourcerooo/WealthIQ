using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WealthIQ.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TaxAssetClassMetalEtc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Both physically-backed gold products in the shipped data are ETCs — secured debt
            // securities with limited recourse, not fund units — but they are typed ETF_METAL
            // rather than ETC, so the ETC branch of the TaxAssetClass migration never matched them.
            // The InvStG does not apply to them: no Vorabpauschale, no Teilfreistellung, and their
            // gains belong on Anlage KAP Zeile 19 rather than Anlage KAP-INV.
            migrationBuilder.Sql(@"
                UPDATE InstrumentProfiles
                   SET TaxAssetClass = 'other_security',
                       SubjectToVorabpauschale = 0
                 WHERE Type = 'ETF_METAL';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Deliberately NOT reverted: this is a data correction, not a schema change, and
            // re-introducing the known-wrong SubjectToVorabpauschale flag on a rollback would be
            // worse than leaving the corrected value in place — mirrors the reasoning in the
            // ETC branch of the TaxAssetClass migration.
        }
    }
}
