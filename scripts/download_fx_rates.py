#!/usr/bin/env python3
from __future__ import annotations

import csv
import datetime as dt
import pathlib
import sys
import urllib.request
import xml.etree.ElementTree as ET


REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent
DEFAULT_OUTPUT = REPO_ROOT / "data" / "old_project" / "Frontend" / "ConsoleUi" / "Sigmatic.Console" / "Input" / "Configuration" / "fx_rates.csv"

ECB_HISTORICAL_URL = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-hist.xml"
SUPPORTED_CURRENCIES = {"USD", "GBP", "CHF"}


def fetch_ecb_rows(start: dt.date, end: dt.date) -> list[tuple[str, str, str]]:
    request = urllib.request.Request(ECB_HISTORICAL_URL, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(request, timeout=60) as response:
        xml_payload = response.read().decode("utf-8")

    namespace = {
        "gesmes": "http://www.gesmes.org/xml/2002-08-01",
        "def": "http://www.ecb.int/vocabulary/2002-08-01/eurofxref",
    }
    root = ET.fromstring(xml_payload)

    rows: list[tuple[str, str, str]] = []
    for day_cube in root.findall(".//def:Cube[@time]", namespace):
        date_text = day_cube.attrib["time"]
        date_value = dt.date.fromisoformat(date_text)
        if date_value < start or date_value > end:
            continue

        rows.append((date_text, "EUR", "1.0000000000"))
        for rate_cube in day_cube.findall("def:Cube[@currency][@rate]", namespace):
            currency = rate_cube.attrib["currency"]
            if currency not in SUPPORTED_CURRENCIES:
                continue

            eur_to_currency_rate = float(rate_cube.attrib["rate"])
            currency_to_eur_rate = 1.0 / eur_to_currency_rate
            rows.append((date_text, currency, f"{currency_to_eur_rate:.10f}"))

    return rows


def build_fx_rows(start: dt.date, end: dt.date) -> list[tuple[str, str, str]]:
    rows = fetch_ecb_rows(start, end)
    rows.sort(key=lambda item: (item[0], item[1]))
    return rows


def main() -> int:
    output_path = pathlib.Path(sys.argv[1]).resolve() if len(sys.argv) > 1 else DEFAULT_OUTPUT
    output_path.parent.mkdir(parents=True, exist_ok=True)

    today = dt.date.today()
    start = today - dt.timedelta(days=365 * 5 + 7)

    rows = build_fx_rows(start, today)
    with output_path.open("w", newline="", encoding="utf-8") as handle:
        writer = csv.writer(handle)
        writer.writerow(["date", "currency", "rate_to_eur"])
        writer.writerows(rows)

    print(f"Wrote {len(rows)} FX rows to {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
