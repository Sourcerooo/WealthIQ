using System;
using System.Collections.Generic;
using System.Text;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Ledger;
using WealthIQ.Domain.Model.Matching;
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Domain.Interface.Matcher;

public interface ILotMatcher
{
    TradeMatchResult Match(
      TradeEntry tradeEntry,
      IReadOnlyList<OpenLot> currentOpenLots,
      LotMatchingPolicy policy);
}
