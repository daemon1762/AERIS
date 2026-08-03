#!/usr/bin/env python3
"""Regression model for the v0.17.0.2 stock runway-registry identity hotfix."""
from __future__ import annotations

import re
import sys
from dataclasses import dataclass

sys.dont_write_bytecode = True

from v01700_testlib import SOURCE, CheckSuite, read


@dataclass(frozen=True)
class Record:
    source: str
    site_id: str
    group: str
    display_name: str
    uuid: str = ""


def uses_provider_airfield_group(record: Record) -> bool:
    return record.source in {"KerbalKonstructs", "StockLaunchsitesExpansion"}


def discovered_identity(record: Record) -> str:
    if uses_provider_airfield_group(record) and record.group:
        return record.group
    return record.site_id or record.uuid or record.display_name or "UNNAMED"


def sanitize(value: str) -> str:
    text = re.sub(r"[^A-Za-z0-9]+", "_", value.strip()).strip("_")
    return text.upper() or "UNNAMED"


def airfield_id(record: Record) -> str:
    return "DISC_" + record.source.upper() + "_" + sanitize(
        discovered_identity(record))


suite = CheckSuite("v0.17.0.2 stock/DLC runway-registry identity regression")

# This is the shape that failed in the attached runtime log: independent KSP
# facilities all arrived with the same historical ProviderGroup value ("KSP").
stock_records = [
    Record("Stock", "Runway", "KSP", "Runway"),
    Record("Stock", "KSC Pad", "KSP", "KSC Pad"),
    Record("Stock", "VAB", "KSP", "VAB"),
    Record("Stock", "Island Airfield", "KSP", "Island Airfield"),
]
stock_ids = [airfield_id(record) for record in stock_records]
suite.equal(len(set(stock_ids)), len(stock_ids),
            "independent stock facilities remain unique despite shared legacy KSP group")
suite.check("DISC_STOCK_KSP" not in stock_ids,
            "the attached-log collision ID DISC_STOCK_KSP cannot be generated")
suite.equal(stock_ids[0], "DISC_STOCK_RUNWAY",
            "stock runway identity is provider-site based")
suite.equal(stock_ids[3], "DISC_STOCK_ISLAND_AIRFIELD",
            "stock Island Airfield identity is provider-site based")

dlc_records = [
    Record("Dlc", "Dessert Airfield", "KSP", "Dessert Airfield"),
    Record("Dlc", "Woomerang Launch Site", "KSP", "Woomerang Launch Site"),
]
suite.equal(len({airfield_id(record) for record in dlc_records}), 2,
            "independent DLC facilities remain unique")

kk_records = [
    Record("KerbalKonstructs", "Area 52 Long runway", "Area 52",
           "Area 52 Long runway", "uuid-long"),
    Record("KerbalKonstructs", "Area 52 X-Runway", "Area 52",
           "Area 52 X-Runway", "uuid-cross"),
]
suite.equal(len({airfield_id(record) for record in kk_records}), 1,
            "multiple Kerbal Konstructs runways at one airport still group together")

sle_records = [
    Record("StockLaunchsitesExpansion", "Glacier Lake Runway",
           "GlacierUpgrades", "Glacier Lake Runway", "uuid-short"),
    Record("StockLaunchsitesExpansion", "Glacier Lake Long Runway",
           "GlacierUpgrades", "Glacier Lake Long Runway", "uuid-long"),
]
suite.equal(len({airfield_id(record) for record in sle_records}), 1,
            "multiple SLE runways at one airport still group together")

provider = read(SOURCE / "Landing" / "AERISAirfieldProviders.cs")
registry = read(SOURCE / "Landing" / "AERISAirfieldRegistry.cs")
window = read(SOURCE / "UI" / "AERISWindow.cs")

for token in (
        'ReadString(facility, "facilityDisplayName")',
        "record.ProviderSiteId = name",
        "record.ProviderGroup = name",
        "record.DisplayName = name",
):
    suite.check(token in provider, "KSP facility identity contract: " + token)
suite.check('record.ProviderGroup = "KSP"' not in provider,
            "literal KSP group collapse is removed")

for token in (
        "UsesProviderAirfieldGroup(record)",
        "DiscoveredAirfieldIdentity(record)",
        "if (record == null || !UsesProviderAirfieldGroup(record)) return null",
        "record.Source == AERISAirfieldSource.KerbalKonstructs",
        "record.Source == AERISAirfieldSource.StockLaunchsitesExpansion",
        "SingleLineStableId(airfield.StableId)",
        "SingleLineStableId(stable)",
):
    suite.check(token in registry, "registry identity/diagnostic contract: " + token)
suite.check(('GUILayout.Label("RESULT "+registry.Status)' in window) or
            ('WrappedAirfieldLabel("RESULT  "+registry.Status)' in window),
            "AIRFIELDS page exposes the full reload result")

suite.finish()
