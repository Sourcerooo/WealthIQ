namespace WealthIQ.Application.Tax.Interface;

public interface IBasisInterestRateProvider
{
    decimal GetRate(int year);
}
