using WealthIQ.Domain.Model.General;
using WealthIQ.Infrastructure.Persistence.Rows;

namespace WealthIQ.Infrastructure.Persistence.Mapping;

public static class AccountMapper
{
    public static AccountRow ToRow(Account account) => new()
    {
        AccountId = account.AccountId.Value,
        AccountNumber = account.AccountNumber
    };

    public static Account ToDomain(AccountRow row) =>
        new(new AccountId(row.AccountId), row.AccountNumber);
}
