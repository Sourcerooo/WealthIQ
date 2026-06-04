namespace WealthIQ.Application.ReferenceData.Interface;

public interface IInstrumentReferenceAdmin
{
    Task<IReadOnlyList<InstrumentAdminDto>> ListAsync(CancellationToken ct = default);
    Task SaveAsync(InstrumentAdminDto instrument, CancellationToken ct = default);
    Task<bool> IsReferencedByLedgerAsync(string isin, CancellationToken ct = default);
    Task DeleteAsync(string isin, CancellationToken ct = default);
    Task<InstrumentUploadResult> UploadAsync(string instrumentsJson, string listingsJson, UploadMode mode, CancellationToken ct = default);
}
