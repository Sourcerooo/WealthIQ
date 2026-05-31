using System.Security.Cryptography;
using System.Text;
using WealthIQ.Domain.Model.General;

namespace WealthIQ.Web.Composition;

/// <summary>
/// Derives a stable <see cref="AccountId"/> from (broker, account number) so that re-importing the same
/// account upserts one account row instead of creating duplicates (dedup itself is by source reference).
/// </summary>
public static class DeterministicAccount
{
    public static AccountId IdFor(string broker, string accountNumber)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{broker}:{accountNumber}"));
        return new AccountId(new Guid(hash.AsSpan(0, 16)));
    }
}
