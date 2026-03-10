#!/usr/bin/env python3
"""
Python version of the fish data generator.
Generates a merged fish dataset at: AutoHook/Data/FishData/fish_list.json

Fishing data comes from Teamcraft.
Spearfishing size/speed is enriched from GatherBuddy's Apache-2.0 fish dataset.

Dependencies:
  pip install requests

Run:
    python generate_fish_json.py

Note: this script does not attempt to work around certificate issues.
"""
import json
import os
import re
from datetime import datetime, timedelta

import requests

GRAPHQL = "https://gubal.ffxivteamcraft.com/graphql"
GITHUB_API = "https://api.github.com"
EORZEA_HOUR_MS = 175000
EORZEA_DAY_MS = 24 * EORZEA_HOUR_MS
GATHERBUDDY_REPO_API = f"{GITHUB_API}/repos/Ottermandias/GatherBuddy/contents/GatherBuddy.GameData/Data/Fish"

GATHERBUDDY_SIZE_TO_AUTOHOOK = {
    "Unknown": 255,
    "Small": 1,
    "Average": 2,
    "Large": 3,
    "None": 255,
}

GATHERBUDDY_SPEED_TO_AUTOHOOK = {
    "Unknown": 65535,
    "SuperSlow": 100,
    "ExtremelySlow": 150,
    "VerySlow": 200,
    "Slow": 250,
    "Average": 300,
    "Fast": 350,
    "VeryFast": 400,
    "ExtremelyFast": 450,
    "SuperFast": 500,
    "HyperFast": 550,
    "LynFast": 600,
    "None": 65535,
}

GATHERBUDDY_SPEAR_PATTERN = re.compile(
    r"data\.Apply\s*\(\s*(\d+)\s*,[^;]*?\.Spear\s*\(\s*data\s*,\s*SpearfishSize\.(\w+)\s*,\s*SpearfishSpeed\.(\w+)\s*\)[^;]*;",
    re.DOTALL,
)

TEAMCRAFT_TUG_TO_BITE_TYPE = {
    0: 37,  # Strong
    1: 38,  # Legendary
    2: 36,  # Weak
}

TEAMCRAFT_HOOKSET_TO_HOOK_TYPE = {
    1: 4103,  # Powerful Hookset
    2: 4179,  # Precision Hookset
}


def time_range_string(spawn, duration):
    if spawn is None or duration is None:
        return None
    # spawn is hours as integer (Eorzea hour), convert to HH:MM
    base = datetime(2000, 1, 1, int(spawn) % 24, 0)
    end = base + timedelta(hours=float(duration))
    return f"{base.strftime('%H:%M')}–{end.strftime('%H:%M')}"


def build_legacy_interval(spawn, duration):
    if spawn is None or duration is None:
        return {"OnTime": 1, "OffTime": 0, "ShiftTime": 0}

    on_time = max(int(round(float(duration) * EORZEA_HOUR_MS)), 1)
    shift_time = int(round(float(spawn) * EORZEA_HOUR_MS)) % EORZEA_DAY_MS
    off_time = max(EORZEA_DAY_MS - on_time, 0)
    return {"OnTime": on_time, "OffTime": off_time, "ShiftTime": shift_time}


def map_bite_type(tug, is_spearfishing):
    if tug is None:
        return 255 if is_spearfishing else 0
    return TEAMCRAFT_TUG_TO_BITE_TYPE.get(int(tug), 0)


def map_hook_type(hookset, is_spearfishing):
    if hookset is None:
        return 255 if is_spearfishing else 0
    return TEAMCRAFT_HOOKSET_TO_HOOK_TYPE.get(int(hookset), 0)


def fetch_graphql(query, variables=None):
    payload = {"query": query, "variables": variables or {}}
    r = requests.post(GRAPHQL, json=payload, headers={"Content-Type": "application/json"})
    r.raise_for_status()
    return r.json()


def fetch_json(url):
    r = requests.get(url, headers={"Accept": "application/vnd.github+json"}, timeout=30)
    r.raise_for_status()
    return r.json()


def fetch_text(url):
    r = requests.get(url, timeout=30)
    r.raise_for_status()
    return r.text


def fetch_gatherbuddy_spearfish_map():
    contents = fetch_json(GATHERBUDDY_REPO_API)
    file_entries = sorted(
        (
            entry for entry in contents
            if entry.get("type") == "file" and re.fullmatch(r"Data\d+\.\d+\.cs", entry.get("name", ""))
        ),
        key=lambda entry: entry["name"],
    )

    spearfish_map = {}
    for entry in file_entries:
        source = fetch_text(entry["download_url"])
        for item_id, size_name, speed_name in GATHERBUDDY_SPEAR_PATTERN.findall(source):
            size_value = GATHERBUDDY_SIZE_TO_AUTOHOOK.get(size_name)
            speed_value = GATHERBUDDY_SPEED_TO_AUTOHOOK.get(speed_name)
            if size_value is None or speed_value is None:
                continue
            spearfish_map[int(item_id)] = (size_value, speed_value)

    return spearfish_map


def fetch_bite_times():
    query = '''query AllBiteTimes {
      biteTimes: bite_time_per_fish_per_spot(
        where: { flooredBiteTime: {_gt: 1, _lt: 600}, occurences: {_gte: 3} },
        order_by: { itemId: asc }
      ) {
        itemId
        spot
        flooredBiteTime
        occurences
      }
    }'''
    j = fetch_graphql(query)
    return j.get("data", {}).get("biteTimes", [])


def fetch_allagan_reports_all():
    query = '''query AllAllaganReports {
      allagan_reports {
        itemId
        source
        data
      }
    }'''
    j = fetch_graphql(query)
    rows = j.get("data", {}).get("allagan_reports", [])

    mp = {}
    for row in rows:
        src = row.get("source")
        if src not in ("FISHING", "SPEARFISHING"):
            continue
        item_id = int(row.get("itemId"))
        data = row.get("data")
        if isinstance(data, str):
            try:
                data = json.loads(data)
            except Exception:
                continue
        if not isinstance(data, dict):
            continue
        mp.setdefault(item_id, []).append({"source": src, "data": data})
    return mp


def main():
    print("Fetching bite times from Teamcraft...")
    bite_rows = fetch_bite_times()
    print(f"Fetched {len(bite_rows)} bite-time rows.")

    print("Fetching exact spearfish size/speed data from GatherBuddy...")
    gatherbuddy_spearfish = fetch_gatherbuddy_spearfish_map()
    print(f"Fetched GatherBuddy spearfish mappings for {len(gatherbuddy_spearfish)} fish.")

    bite_map = {}
    for r in bite_rows:
        try:
            item = int(r.get("itemId"))
            time = int(r.get("flooredBiteTime"))
            count = int(r.get("occurences"))
        except Exception:
            continue
        bite_map.setdefault(item, {})
        bite_map[item][time] = bite_map[item].get(time, 0) + count

    print("Fetching all Allagan reports (single query)...")
    allagan_map = fetch_allagan_reports_all()
    print(f"Fetched reports for {len(allagan_map)} items.")

    fish_ids = sorted(set(list(bite_map.keys()) + list(allagan_map.keys())))
    print(f"Processing {len(fish_ids)} fish (union of bite data and reports).")

    results = []
    replaced_spearfish_values = 0
    for i, fish_id in enumerate(fish_ids):
        if i % 50 == 0:
            print(f"Processed {i}/{len(fish_ids)}")

        times = sorted(int(t) for t in bite_map.get(fish_id, {}).keys())
        bite_time_min = float(times[0]) if times else 0.0
        bite_time_max = float(times[-1]) if times else 0.0

        reports = allagan_map.get(fish_id, [])
        is_spearfishing = any(r.get("source") == "SPEARFISHING" for r in reports)

        combined = {}
        for r in reports:
            data = r.get("data") or {}
            # spots
            if data.get("spots"):
                combined.setdefault("spots", set()).update(data.get("spots") or [])
            if data.get("spot"):
                combined.setdefault("spots", set()).add(data.get("spot"))
            if data.get("bait") is not None:
                combined.setdefault("bait", data.get("bait"))
            if data.get("tug") is not None:
                combined.setdefault("tug", data.get("tug"))
            if data.get("hookset") is not None:
                combined.setdefault("hookset", data.get("hookset"))
            if data.get("weathers"):
                combined.setdefault("weathers", data.get("weathers"))
            if data.get("weathersFrom"):
                combined.setdefault("weathersFrom", data.get("weathersFrom"))
            if data.get("spawn") is not None:
                combined.setdefault("spawn", data.get("spawn"))
            if data.get("duration") is not None:
                combined.setdefault("duration", data.get("duration"))
            if data.get("minGathering") is not None:
                combined.setdefault("minGathering", data.get("minGathering"))
            if data.get("snagging") is not None:
                combined.setdefault("snagging", bool(data.get("snagging")))
            if data.get("mLure") is not None:
                combined["mLure"] = max(combined.get("mLure", 0), int(data.get("mLure")))
            if data.get("aLure") is not None:
                combined["aLure"] = max(combined.get("aLure", 0), int(data.get("aLure")))
            if data.get("oceanFishingTime") is not None:
                combined.setdefault("oceanFishingTime", data.get("oceanFishingTime"))
            if data.get("fruityVideo"):
                combined.setdefault("fruityVideo", data.get("fruityVideo"))
            if data.get("speed") is not None:
                combined.setdefault("speed", data.get("speed"))
            if data.get("shadowSize") is not None:
                combined.setdefault("shadowSize", data.get("shadowSize"))
            if data.get("predators"):
                combined.setdefault("predators", {})
                for p in data.get("predators"):
                    try:
                        pid = int(p.get("id"))
                        amt = int(p.get("amount", 1))
                    except Exception:
                        continue
                    combined["predators"][pid] = max(combined["predators"].get(pid, 0), amt)

        # finalize sets
        spots = sorted(list(combined.get("spots", []))) if combined.get("spots") else []
        size_value = int(combined.get("shadowSize")) if is_spearfishing and combined.get("shadowSize") is not None else 255
        speed_value = int(combined.get("speed")) if is_spearfishing and combined.get("speed") is not None else 65535

        if is_spearfishing and fish_id in gatherbuddy_spearfish:
            size_value, speed_value = gatherbuddy_spearfish[fish_id]
            replaced_spearfish_values += 1

        entry = {
            "ItemId": int(fish_id),
            "HookType": map_hook_type(combined.get("hookset"), is_spearfishing),
            "BiteType": map_bite_type(combined.get("tug"), is_spearfishing),
            "InitialBait": int(combined.get("bait")) if combined.get("bait") is not None else 0,
            "Mooches": [],
            "Predators": [{"ItemId": int(k), "Quantity": int(v)} for k, v in (combined.get("predators") or {}).items()],
            "Nodes": [],
            "IsSpearFish": bool(is_spearfishing),
            "Size": size_value,
            "Speed": speed_value,
            "SurfaceSlap": 0,
            "OceanFish": combined.get("oceanFishingTime") is not None,
            "SpotIds": [int(s) for s in spots],
            "Weathers": combined.get("weathers") or [],
            "WeathersFrom": combined.get("weathersFrom") or [],
            "Spawn": combined.get("spawn") if combined.get("spawn") is not None else None,
            "Duration": combined.get("duration") if combined.get("duration") is not None else None,
            "Time": time_range_string(combined.get("spawn"), combined.get("duration")),
            "MinGathering": combined.get("minGathering") if combined.get("minGathering") is not None else None,
            "Snagging": bool(combined.get("snagging")),
            "MLure": int(combined.get("mLure", 0)),
            "ALure": int(combined.get("aLure", 0)),
            "OceanFishingTime": combined.get("oceanFishingTime") if combined.get("oceanFishingTime") is not None else None,
            "FruityVideo": combined.get("fruityVideo") if combined.get("fruityVideo") is not None else None,
            "BiteTimeMin": bite_time_min,
            "BiteTimeMax": bite_time_max,
        }

        results.append(entry)

    # build mooch sequences (include candidate set of reported itemIds)
    candidate_set = {int(r["ItemId"]) for r in results} | set(allagan_map.keys())
    bait_map = {}
    for r in results:
        if r.get("InitialBait") and int(r.get("InitialBait")) in candidate_set:
            bait_map[int(r["ItemId"])] = int(r["InitialBait"])

    def build_mooch(start_id):
        seq = []
        seen = set()
        cur = bait_map.get(start_id)
        while cur and cur in candidate_set and cur not in seen:
            seq.append(cur)
            seen.add(cur)
            cur = bait_map.get(cur)
        return seq

    for r in results:
        r["Mooches"] = build_mooch(r["ItemId"])

    output_dir = os.path.join('AutoHook', 'Data', 'FishData')
    os.makedirs(output_dir, exist_ok=True)
    output_path = os.path.join(output_dir, 'fish_list.json')
    with open(output_path, 'w', encoding='utf-8') as f:
        json.dump(results, f, indent=2)

    print(f"Wrote {len(results)} fish entries to {output_path}")
    print(f"Replaced GatherBuddy spearfish size/speed for {replaced_spearfish_values} entries.")


if __name__ == "__main__":
    main()



