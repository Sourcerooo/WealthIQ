namespace WealthIQ.Application.ReferenceData.Interface;

/// <summary>
/// Seeds reference data from the shipped files into the database. Seed-if-empty per table, so
/// calling it repeatedly is safe (idempotent) and never overwrites later user edits.
/// </summary>
public interface IReferenceDataSeeder
{
    Task<ReferenceDataSeedResult> SeedIfEmptyAsync(ReferenceDataSources sources, CancellationToken ct = default);
}
