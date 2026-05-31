# Lot Matching Design

This document describes the currently implemented FIFO lot matching behavior.

## Entry Point

- `FiFoMatcher.Match(TradeEntry tradeEntry, IReadOnlyList<OpenLot> currentOpenLots, LotMatchingPolicy policy)`

## Matching Rules Implemented Today

- `Sell` closes `Long` lots.
- `Buy` closes `Short` lots.
- Matching is filtered by both `AccountId` and `InstrumentId`.
- Eligible lots are sorted by `OpenOccurredAt` ascending.
- Matching consumes as much quantity as possible from the oldest eligible lot first.
- If the closing trade quantity exceeds all eligible open lots, a new remainder lot is opened in the closing trade direction.
- If no eligible opposite-direction lot exists, the whole trade becomes a new open lot.

## Cost Allocation

- Open-lot fees and taxes are reduced pro rata through `OpenLot.Consume(...)`.
- Close-side fees and taxes are allocated to each consumption by matched-quantity ratio.
- `LotConsumption` derives:
  - `CostBasis`
  - `Proceeds`
  - `RealizedPnL`

## Current Scope Notes

- The method accepts `LotMatchingPolicy`, but only FIFO behavior is implemented.
- Matching logic supports both long and short realization paths.
- `OpenLot` also carries accumulated `Vorabpauschale`, which is reduced proportionally when a lot is partially consumed.
