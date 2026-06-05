namespace WealthIQ.Application.Tax.Interface;

public interface IBasisInterestRateProvider
{
    /// <summary>The Basiszins for <paramref name="year"/>. <c>null</c> = no value on file
    /// (a data gap → blocking error if the year is in replay scope); <c>≤ 0</c> = an official
    /// zero/negative rate (skip the year, no price lookup); <c>&gt; 0</c> = compute. (spec §5.3)</summary>
    decimal? GetRate(int year);
}
