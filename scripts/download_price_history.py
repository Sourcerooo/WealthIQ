#!/usr/bin/env python3
from __future__ import annotations

import csv
import datetime as dt
import json
import pathlib
import sys
import urllib.parse
import urllib.request


REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_MAPPING = REPO_ROOT / "data" / "reference" / "market_data_mappings.json"
DEFAULT_OUTPUT = REPO_ROOT / "data" / "reference" / "historical_prices.csv"


def fetch_history(symbol: str, start: dt.date, end: dt.date) -> list[dict[str, str]]:
    period1 = int(dt.datetime.combine(start, dt.time.min, tzinfo=dt.timezone.utc).timestamp())
    period2 = int(dt.datetime.combine(end + dt.timedelta(days=1), dt.time.min, tzinfo=dt.timezone.utc).timestamp())
    params = urllib.parse.urlencode(
        {
            "period1": period1,
            "period2": period2,
            "interval": "1d",
            "includePrePost": "false",
            "events": "history",
        }
    )
    url = f"https://query1.finance.yahoo.com/v8/finance/chart/{urllib.parse.quote(symbol)}?{params}"
    request = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        payload = json.loads(response.read().decode("utf-8"))

    result = payload.get("chart", {}).get("result")
    if not result:
        error = payload.get("chart", {}).get("error")
        raise RuntimeError(f"Yahoo Finance returned no result for {symbol}: {error}")

    result = result[0]
    meta = result.get("meta", {})
    timestamps = result.get("timestamp", [])
    quote = result.get("indicators", {}).get("quote", [{}])[0]
    adjusted = result.get("indicators", {}).get("adjclose", [{}])[0]
    currency = meta.get("currency")
    if not currency:
        raise RuntimeError(f"Yahoo Finance returned no currency for {symbol}")

    rows: list[dict[str, str]] = []
    for index, timestamp in enumerate(timestamps):
        open_value = quote.get("open", [None])[index]
        high_value = quote.get("high", [None])[index]
        low_value = quote.get("low", [None])[index]
        close_value = quote.get("close", [None])[index]
        adjusted_close = adjusted.get("adjclose", [None])[index]
        volume = quote.get("volume", [None])[index]
        if None in (open_value, high_value, low_value, close_value, adjusted_close, volume):
            continue

        trade_date = dt.datetime.fromtimestamp(timestamp, tz=dt.timezone.utc).date().isoformat()
        rows.append(
            {
                "date": trade_date,
                "provider_symbol": symbol,
                "currency": currency,
                "open": f"{float(open_value):.10f}",
                "high": f"{float(high_value):.10f}",
                "low": f"{float(low_value):.10f}",
                "close": f"{float(close_value):.10f}",
                "adjusted_close": f"{float(adjusted_close):.10f}",
                "volume": str(int(volume)),
            }
        )

    if not rows:
        raise RuntimeError(f"Yahoo Finance returned no usable OHLCV rows for {symbol}")

    return rows


def main() -> int:
    mapping_path = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else DEFAULT_MAPPING
    output_path = pathlib.Path(sys.argv[2]).resolve() if len(sys.argv) > 2 else DEFAULT_OUTPUT

    if not mapping_path.exists():
        raise FileNotFoundError(f"Market-data mapping file not found: {mapping_path}")

    with mapping_path.open("r", encoding="utf-8") as handle:
        mappings = json.load(handle)

    today = dt.date.today()
    start = today - dt.timedelta(days=365 * 5 + 7)
    all_rows: list[dict[str, str]] = []

    for isin, config in mappings.items():
        symbol = config["provider_symbol"]
        rows = fetch_history(symbol, start, today)
        for row in rows:
            row["isin"] = isin
        all_rows.extend(rows)

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(
            handle,
            fieldnames=["date", "provider_symbol", "currency", "open", "high", "low", "close", "adjusted_close", "volume", "isin"],
        )
        writer.writeheader()
        writer.writerows(sorted(all_rows, key=lambda row: (row["provider_symbol"], row["date"])))

    print(f"Wrote {len(all_rows)} OHLCV rows to {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
