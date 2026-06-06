using WealthIQ.Domain.Enumeration;
using WealthIQ.Domain.Model.General;
using WealthIQ.Domain.Model.Tax;
using Xunit;

namespace WealthIQ.Tests.Domain;

public sealed class GermanTaxEntryTests
{
    [Fact]
    public void NewEntry_DefaultsAccountAndKestToZero()
    {
        var entry = new GermanTaxEntry(2025, new DateOnly(2025, 1, 1),
            GermanTaxEntryType.Sell, "AAA", "DE0001", 100m, 70m);

        Assert.Equal(default(AccountId), entry.AccountId);
        Assert.Equal(0m, entry.WithheldKESt);
    }

    [Fact]
    public void NewEntry_CanCarryAccountAndKest()
    {
        var account = AccountId.NewId();
        var entry = new GermanTaxEntry(2025, new DateOnly(2025, 1, 1),
            GermanTaxEntryType.Sell, "AAA", "DE0001", 100m, 70m,
            AccountId: account, WithheldKESt: 12.34m);

        Assert.Equal(account, entry.AccountId);
        Assert.Equal(12.34m, entry.WithheldKESt);
    }
}
