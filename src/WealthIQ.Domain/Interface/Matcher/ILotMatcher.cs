using System;
using System.Collections.Generic;
using System.Text;
using WealthIQ.Domain.Model.Event;
using WealthIQ.Domain.Model.Lot;
using WealthIQ.Domain.Model.Matching;
using WealthIQ.Domain.Enumeration;

namespace WealthIQ.Domain.Interface.Matcher;

public interface ILotMatcher
{
    TradeMatchResult Match(
      ExecutedTradeEvent tradeEvent,
      IReadOnlyList<OpenLot> currentOpenLots,
      LotMatchingPolicy policy);
}
