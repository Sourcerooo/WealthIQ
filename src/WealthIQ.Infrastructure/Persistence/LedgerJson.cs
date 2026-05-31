using System.Text.Json;
using System.Text.Json.Serialization;

namespace WealthIQ.Infrastructure.Persistence;

/// <summary>
/// Shared System.Text.Json options for serializing concrete ledger entries.
/// Enums (e.g. Currency, TradeSide) are stored as strings for readability.
/// </summary>
internal static class LedgerJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };
}
