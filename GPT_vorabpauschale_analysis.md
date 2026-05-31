# GPT Vorabpauschale Analysis

Date: 2026-05-31

Scope: independent review of `docs/superpowers/specs/2026-05-31-phase2-data-administration-design.md`, focused on whether the described Vorabpauschale bug is real, whether the proposed correction fixes it, and whether the design covers the relevant edge cases. The spec document itself was not changed.

## Executive conclusion

Claude's core bug finding is valid. The current implementation calculates Vorabpauschale for every holding year from the lot's original acquisition cost. For non-acquisition years this is logically wrong under §18 InvStG, because the Basisertrag uses the Rücknahmepreis at the beginning of the relevant calendar year and the statutory cap uses the first and last Rücknahmepreis in that calendar year.

The spec's main correction, re-basing non-acquisition years to the first price in year Y and comparing to the last price in year Y, fixes that specific multi-year buy-and-hold bug.

However, the spec does not cover all cases. I found several issues and remarks that should be addressed before implementation, especially the treatment of distributions in the statutory cap and the need to apply Vorabpauschale only to investment funds, not every security with an ISIN.

## Sources checked

- Current implementation: `src/WealthIQ.Application/Tax/GermanTaxCalculator.cs`, especially `PerformYearEndClosing` and `CalculateRemainingAcquisitionPriceInEur`.
- Current tests: `tests/WealthIQ.Tests/Application/Tax/GermanTaxCalculatorVorabpauschaleTests.cs`, `GermanTaxCalculatorTests.cs`, `GermanTaxRegressionTests.cs`.
- Current data interfaces: `IYearEndPriceProvider`, `CsvHistoricalPriceLookup`, `FxConverter`.
- Statutory text: §18 InvStG and §19 InvStG from `gesetze-im-internet.de`.

## Verification of the described bug

Current code behavior:

- `PerformYearEndClosing` groups open long lots by instrument and loads one `yearEndPrice` per ISIN/year.
- For every open lot, it calculates `acquisitionPrice = CalculateRemainingAcquisitionPriceInEur(lot)`.
- It then uses that acquisition price for both:
  - `basisYield = acquisitionPrice * basisFactor * (months / 12)`
  - `appreciation = Max(0, yearEndPrice - acquisitionPrice)`
- `months` is pro-rated only when `lot.OpenTradeDate.Year == year`; otherwise it is `12`.

That means a lot bought in 2022 and held through 2024 still uses its 2022 acquisition cost as the 2024 starting base. That is the bug described in the spec.

Why this is logically wrong:

- §18(1) InvStG says the Basisertrag is calculated from the Rücknahmepreis at the beginning of the calendar year.
- §18(1) also says the cap is based on the difference between the first and last redemption price fixed in the calendar year, plus distributions.
- Therefore, for a position already held before year Y, the 2024 calculation cannot use the original 2022 acquisition cost as the 2024 start value.

Simple example showing the bug:

```text
Buy in 2022: 100 EUR
2024 first price: 150 EUR
2024 last price: 140 EUR
Basiszins: positive

Current code:
start base = 100, appreciation = 140 - 100 = 40 => may tax Vorabpauschale

Correct year-Y logic:
start base = 150, appreciation = 140 - 150 = -10 => no Vorabpauschale
```

So the current implementation can tax a year with an actual within-year loss merely because the fund remains above its original acquisition cost. That is a real logical defect.

## Validation of the proposed core fix

The proposed split is correct for the multi-year issue:

- Acquisition year: keep the current acquisition-cost/pro-rated-month path.
- Non-acquisition years: use the first year-Y price as the per-share start value and the last year-Y price as the per-share end value.
- Convert each price at its own quote date, which is consistent with the project's existing FX rule.
- Apply the result to `RemainingQuantity` of each still-open long lot.
- Keep full raw Vorabpauschale in `AccumulatedVorabpauschale`; §19(1) says the sale gain is reduced by the full Vorabpauschale, irrespective of Teilfreistellung.

The data-model changes support the fix:

- The current `IYearEndPriceProvider` cannot supply year-start prices and is keyed only by `(ISIN, year)`.
- Historical prices plus `(ISIN, currency)` listings are a sensible prerequisite.
- Using `Close` instead of `AdjustedClose` is important because distributions are handled separately in the tax formula.
- Resolving prices by `(ISIN, lot currency)` fixes the existing risk of mixing listing currencies.

## Findings and remarks

### 1. The distribution cap formula is likely still wrong

Severity: high.

The spec keeps the current formula:

```text
grossVorab = min(basisErtrag, wertsteigerung)
netVorab   = max(0, grossVorab - distributions)
```

But §18(1) InvStG says the Basisertrag is capped by the increase between first and last Rücknahmepreis plus distributions within the calendar year. Algebraically, this is:

```text
cap        = max(0, endValue - startValue + distributions)
grossBase  = min(basisErtrag, cap)
netVorab   = max(0, grossBase - distributions)
```

The current/spec formula is equivalent only in some cases, for example when distributions are zero or when distributions fully eliminate the Vorabpauschale. It is not equivalent in general.

Counterexample:

```text
startValue      = 100.00
endValue        = 101.00
appreciation    = 1.00
distributions   = 0.50
basisErtrag     = 3.50

Spec/current:
min(3.50, 1.00) - 0.50 = 0.50

§18 wording:
min(3.50, 1.00 + 0.50) - 0.50 = 1.00
```

This means the spec can understate Vorabpauschale where distributions exist and the appreciation cap binds. If the project intentionally chooses the existing simplified formula, the spec should call it out as a deliberate assumption. Otherwise, the correction should include distributions in the cap.

### 2. Vorabpauschale should not apply to every ISIN-bearing long lot

Severity: high.

Current code applies Vorabpauschale to every open long lot whose instrument has a non-empty ISIN. The importer accepts both `STK` and `FUND`, and Phase 2 adds `InstrumentProfile.Type`, but the corrected algorithm still says "for each open long lot" rather than "for each open long investment fund lot".

Vorabpauschale under InvStG applies to investment funds, not ordinary stocks. If the application imports an individual stock with an ISIN and a positive Basiszins, the current pattern can require a year-end price and create a Vorabpauschale where none should exist.

Recommended design change:

```text
Only compute Vorabpauschale for instruments classified as investment funds / ETFs / applicable InvStG instruments.
Explicitly skip ordinary stocks, cash, and unsupported instrument types.
```

Add a targeted test such as:

```text
Vorabpauschale_OrdinaryStockWithIsin_IsSkipped
```

### 3. Missing Basiszins is currently indistinguishable from a zero/negative Basiszins

Severity: medium.

Current providers return `0` for an unknown basis-interest year, and the calculator immediately skips Vorabpauschale when `basisInterestRate <= 0`.

That is correct for years with an official zero or negative basis rate, but it is dangerous for a missing positive-rate year. A missing `BasisInterestRate` record could silently suppress Vorabpauschale.

Phase 2's fail-fast data-administration philosophy suggests this should be explicit:

- Known official zero/negative rate: no Vorabpauschale and no price lookup required.
- Missing basis-rate record for a year in replay scope: blocking error, unless the year is intentionally unsupported/future.

This likely requires changing the provider contract from `decimal GetRate(year)` to something nullable/result-like, or adding a separate `HasRate(year)` behavior.

### 4. Zero/negative Basiszins should short-circuit before price lookups

Severity: medium.

The spec's pseudocode starts by obtaining prices after calculating `basisFactor`, but it does not explicitly preserve the current early return for `basisInterestRate <= 0`.

For years such as 2021/2022, the calculator should not require year-start/year-end prices merely to conclude that no Vorabpauschale exists. The edge-case list says missing year-start/year-end bars are blocking, but that should be conditional on Vorabpauschale being computationally required.

Recommended wording:

```text
If the official Basiszins for Y is <= 0, skip Y's Vorabpauschale calculation before resolving quotes.
```

### 5. The Jan 1 posting date does not match §18(3)'s first-working-day rule

Severity: low to medium.

The current code and spec post Vorabpauschale on `Y+1-01-01`. §18(3) says it is deemed received on the first working day of the following calendar year.

This usually does not change the tax year, but it can make the report date legally imprecise. If the report is intended to be Finanzamt-grade, the spec should either:

- implement first-working-day dating, or
- explicitly document that Jan 1 is a reporting simplification and only the tax year is material.

### 6. Acquisition-year handling is a project assumption, not fully proven by the reviewed statutory text

Severity: low.

The spec says the acquisition year is already correct and should remain based on acquisition cost plus fees, pro-rated by months. That is consistent with current project behavior and is documented as an assumption.

However, §18(1) itself speaks about the Rücknahmepreis at the beginning of the calendar year and the first/last Rücknahmepreis in the calendar year; §18(2) then reduces the Vorabpauschale by full months before acquisition. The reviewed text alone does not explicitly say that acquisition cost including fees replaces the Rücknahmepreis in the acquisition year.

I would not block Phase 2 on this because it is deliberately out of scope and existing behavior, but the spec should avoid saying this is unquestionably proven unless there is an external tax-source confirmation. The current assumption is reasonable, but it is still an assumption.

### 7. FX handling is internally consistent, but still an explicit tax interpretation

Severity: low.

The spec's per-component FX conversion is consistent with WealthIQ's architecture rule: convert at each event or quote date, never at accumulation time. That is also technically clean because it preserves the calculator as the only FX conversion point.

This is still an interpretation for a notional tax item. Another possible treatment would compute the fund-currency Vorabpauschale first and convert the resulting amount at the deemed inflow date. The spec already documents and rejects that alternative, which is good. I found no implementation contradiction here, just a tax-interpretation dependency to keep visible.

## Edge-case coverage assessment

Covered well by the spec:

- Multi-year lots rebase to year-start value.
- Acquisition-year pro-rating remains behavior-preserving.
- Partial sales use `RemainingQuantity` and preserve pro-rata accumulated Vorabpauschale.
- Year-start and year-end prices are currency-aware.
- Missing listings, missing prices, missing FX, and currency mismatch are fail-fast.
- Sale deduction uses full accumulated raw Vorabpauschale, matching §19(1).
- `Close` price is used rather than adjusted close, avoiding dividend double-counting.

Not fully covered:

- Distributions should be included in the statutory cap before subtracting distributions.
- Ordinary stocks with ISIN should be excluded from Vorabpauschale.
- Missing Basiszins should not silently behave like an official zero/negative Basiszins.
- Zero/negative Basiszins should not force price availability.
- First-working-day posting should be implemented or documented as a simplification.
- Acquisition-year acquisition-cost base should remain explicitly documented as an assumption unless further tax-source confirmation is added.

## Suggested implementation-test additions

In addition to the spec's proposed tests, add:

```text
Vorabpauschale_DistributionIncludedInAppreciationCap_WhenCapBinds
Vorabpauschale_OrdinaryStockWithIsin_IsSkipped
Vorabpauschale_MissingBasiszins_ThrowsOrReportsBlockingDiagnostic
Vorabpauschale_NonPositiveOfficialBasiszins_DoesNotRequirePrices
Vorabpauschale_PostingDate_UsesFirstWorkingDayOfFollowingYear
```

The first test is the most important because it can change numbers even after the multi-year rebasing fix is implemented.

## Bottom line

The multi-year rebasing bug is real, and Claude's proposed year-start rebasing is the correct fix for that bug.

But the spec should not be considered complete for all Vorabpauschale cases yet. The distribution-cap formula and fund-only applicability are the two most important gaps to resolve before implementation.
