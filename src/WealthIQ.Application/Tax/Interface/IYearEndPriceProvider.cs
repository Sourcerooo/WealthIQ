namespace WealthIQ.Application.Tax.Interface;

public interface IYearEndPriceProvider
{
    decimal? GetPrice(string isin, int year);
}
