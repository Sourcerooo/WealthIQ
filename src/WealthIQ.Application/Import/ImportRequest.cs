using WealthIQ.Domain.Model.General;

namespace WealthIQ.Application.Import;

public sealed record ImportRequest
{
    public required ImportSource Source { get; init; }
    public required AccountId AccountId { get; init; }
}
