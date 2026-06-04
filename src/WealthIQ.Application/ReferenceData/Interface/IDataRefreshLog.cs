namespace WealthIQ.Application.ReferenceData.Interface;

public interface IDataRefreshLog
{
    Task<DateTimeOffset?> GetLastRefreshedAsync(string dataset, CancellationToken ct = default);
    Task RecordAsync(string dataset, DateTimeOffset whenUtc, string? note, CancellationToken ct = default);
}
