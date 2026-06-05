namespace WealthIQ.Infrastructure.Persistence.Rows;

/// <summary>One row per dataset, upserted on each refresh. Powers the admin page's
/// "last refreshed" status (spec §4).</summary>
public sealed class DataRefreshLogRow
{
    public string Dataset { get; set; } = "";
    public DateTimeOffset LastRefreshedUtc { get; set; }
    public string? Note { get; set; }
}
