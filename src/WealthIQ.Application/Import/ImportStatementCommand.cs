using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

/// <summary>
/// Drives one import: the broker request plus the account the entries belong to.
/// <paramref name="Request"/>.AccountId must equal <paramref name="Account"/>.AccountId.
/// </summary>
public sealed record ImportStatementCommand(ImportRequest Request, Account Account);
