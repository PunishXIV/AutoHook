#!/usr/bin/env python3
"""
Generates the data behind the fish research window:

  AutoHook/Data/FishData/spot_pools.json
      Teamcraft (Gubal) catch counts per spot x bait x lure stacks x fish, misses included,
      plus bite times per spot x bait x fish.

  AutoHook/Data/FishData/fish_tracker.json
      Curated per-fish data (windows, weather, catch path, intuition, lures) from
      Carbuncle Plushy's FF14 Fish Tracker (https://github.com/icykoneko/ff14-fish-tracker-app, MIT).

Dependencies:
  pip install requests

Run:
    python generate_research_data.py
"""
import json
import os

import requests

GRAPHQL = "https://gubal.ffxivteamcraft.com/graphql"
TRACKER_DATA_URL = "https://raw.githubusercontent.com/icykoneko/ff14-fish-tracker-app/master/js/app/data.js"
OUTPUT_DIR = os.path.join("AutoHook", "Data", "FishData")
PAGE_SIZE = 20000

CATCH_COLUMNS = ["bait", "fish", "aLure", "mLure", "count"]
# bite times are grouped per bait/fish: [bait, fish, sec1, count1, sec2, count2, ...]
BITE_COLUMNS = ["bait", "fish", "seconds/count pairs..."]


def fetch_graphql(query, variables=None):
    r = requests.post(GRAPHQL, json={"query": query, "variables": variables or {}}, headers={"Content-Type": "application/json"}, timeout=120)
    r.raise_for_status()
    body = r.json()
    if body.get("errors"):
        raise RuntimeError(f"GraphQL error: {body['errors']}")
    return body["data"]


def fetch_paged(view, fields, where, order_by):
    query = f"""query($limit: Int!, $offset: Int!) {{
      rows: {view}(where: {where}, order_by: {order_by}, limit: $limit, offset: $offset) {{
        {" ".join(fields)}
      }}
    }}"""
    rows, offset = [], 0
    while True:
        page = fetch_graphql(query, {"limit": PAGE_SIZE, "offset": offset})["rows"]
        rows.extend(page)
        print(f"  {view}: {len(rows)} rows")
        if len(page) < PAGE_SIZE:
            return rows
        offset += PAGE_SIZE


def build_spot_pools():
    print("Fetching catches per spot/bait/lure from Teamcraft...")
    catches = fetch_paged(
        "baits_per_fish_per_spot",
        ["spot", "baitId", "itemId", "aLure", "mLure", "occurences"],
        # itemId -1 = nothing bit
        "{ itemId: { _gt: -2 } }",
        "[{ spot: asc }, { baitId: asc }, { itemId: asc }, { aLure: asc }, { mLure: asc }]",
    )

    print("Fetching bite times per spot/bait from Teamcraft...")
    bites = fetch_paged(
        "bite_time_per_fish_per_spot_per_bait",
        ["spot", "baitId", "itemId", "flooredBiteTime", "occurences"],
        # same bounds as generate_fish_json.py
        "{ flooredBiteTime: { _gt: 1, _lt: 600 } }",
        "[{ spot: asc }, { baitId: asc }, { itemId: asc }, { flooredBiteTime: asc }]",
    )

    spots = {}
    for r in catches:
        spot = spots.setdefault(int(r["spot"]), {"Catches": [], "BiteTimes": []})
        spot["Catches"].append([int(r["baitId"]), int(r["itemId"]), int(r["aLure"] or 0), int(r["mLure"] or 0), int(r["occurences"])])
    grouped = {}
    for r in bites:
        key = (int(r["spot"]), int(r["baitId"]), int(r["itemId"]))
        grouped.setdefault(key, []).append((int(r["flooredBiteTime"]), int(r["occurences"])))
    for (spot_id, bait, fish), pairs in grouped.items():
        spot = spots.setdefault(spot_id, {"Catches": [], "BiteTimes": []})
        spot["BiteTimes"].append([bait, fish, *[v for pair in sorted(pairs) for v in pair]])

    for spot in spots.values():
        spot["Catches"].sort()
        spot["BiteTimes"].sort()

    return spots


def write_spot_pools(spots):
    path = os.path.join(OUTPUT_DIR, "spot_pools.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('"Source": "Teamcraft (gubal.ffxivteamcraft.com)",\n')
        f.write(f'"Columns": {json.dumps({"Catches": CATCH_COLUMNS, "BiteTimes": BITE_COLUMNS})},\n')
        f.write('"Spots": {\n')
        keys = sorted(spots)
        for i, key in enumerate(keys):
            sep = "," if i < len(keys) - 1 else ""
            f.write(f'"{key}": {json.dumps(spots[key], separators=(",", ":"))}{sep}\n')
        f.write("}\n}\n")
    print(f"Wrote {len(spots)} spots to {path} ({os.path.getsize(path) // 1024} KB)")


def extract_js_object(source, key):
    """Returns the {...} literal assigned to `key:` in a JS file (the tracker's data.js stores its tables as JSON objects)."""
    start = source.index("{", source.index(f"{key}:"))
    depth = 0
    in_string = False
    escaped = False
    for i in range(start, len(source)):
        c = source[i]
        if in_string:
            if escaped:
                escaped = False
            elif c == "\\":
                escaped = True
            elif c == '"':
                in_string = False
        elif c == '"':
            in_string = True
        elif c == "{":
            depth += 1
        elif c == "}":
            depth -= 1
            if depth == 0:
                return source[start:i + 1]
    raise ValueError(f"Unterminated object for {key}")


def build_fish_tracker():
    print("Fetching Carbuncle Plushy fish tracker data...")
    r = requests.get(TRACKER_DATA_URL, timeout=60)
    r.raise_for_status()
    fish = json.loads(extract_js_object(r.text, "FISH"))

    result = {}
    for item_id, f in fish.items():
        result[int(item_id)] = {
            "Spot": f.get("location"),
            "StartHour": f.get("startHour"),
            "EndHour": f.get("endHour"),
            "WeathersFrom": f.get("previousWeatherSet") or [],
            "Weathers": f.get("weatherSet") or [],
            # each step is a list of alternatives: [[36597, 36596], 36518] -> [[36597, 36596], [36518]]
            "BestCatchPath": [step if isinstance(step, list) else [step] for step in (f.get("bestCatchPath") or [])],
            "Predators": [[int(p[0]), int(p[1])] for p in (f.get("predators") or [])],
            "IntuitionSeconds": f.get("intuitionLength"),
            "Hookset": f.get("hookset"),
            "Tug": f.get("tug"),
            "Lure": f.get("lure"),
            "Snagging": f.get("snagging"),
            "FishEyes": f.get("fishEyes"),
            "BigFish": f.get("bigFish"),
            "Folklore": f.get("folklore"),
            "Collectable": f.get("collectable"),
            "Patch": f.get("patch"),
            "DataMissing": f.get("dataMissing"),
        }
    return result


def write_fish_tracker(fish):
    path = os.path.join(OUTPUT_DIR, "fish_tracker.json")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("{\n")
        f.write('"Source": "FF14 Fish Tracker by Carbuncle Plushy (https://github.com/icykoneko/ff14-fish-tracker-app), MIT License",\n')
        f.write('"Fish": {\n')
        keys = sorted(fish)
        for i, key in enumerate(keys):
            sep = "," if i < len(keys) - 1 else ""
            f.write(f'"{key}": {json.dumps(fish[key], separators=(",", ":"))}{sep}\n')
        f.write("}\n}\n")
    print(f"Wrote {len(fish)} fish to {path}")


def main():
    os.makedirs(OUTPUT_DIR, exist_ok=True)
    write_spot_pools(build_spot_pools())
    write_fish_tracker(build_fish_tracker())


if __name__ == "__main__":
    main()
