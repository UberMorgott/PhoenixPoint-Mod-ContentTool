"""Settle the Wwise name->ID hash algorithm against PP's own SoundbanksInfo.xml.

Usage: python wwise_hash_check.py [--dump index.json]
"""
import sys, json, collections
import xml.etree.ElementTree as ET

XML = (r"D:\Steam\steamapps\common\Phoenix Point\PhoenixPointWin64_Data"
       r"\StreamingAssets\Audio\GeneratedSoundBanks\Windows\SoundbanksInfo.xml")

M = 0xFFFFFFFF

def fnv1(s, lower=True, bits=32):
    b = (s.lower() if lower else s).encode("utf-8")
    h = 2166136261
    for c in b:
        h = (h * 16777619) & M
        h ^= c
    return h & ((1 << bits) - 1)

def fnv1a(s, lower=True, bits=32):
    b = (s.lower() if lower else s).encode("utf-8")
    h = 2166136261
    for c in b:
        h ^= c
        h = (h * 16777619) & M
    return h & ((1 << bits) - 1)

ALGOS = {
    "FNV-1 lower 32":   lambda s: fnv1(s, True, 32),
    "FNV-1 raw 32":     lambda s: fnv1(s, False, 32),
    "FNV-1a lower 32":  lambda s: fnv1a(s, True, 32),
    "FNV-1a raw 32":    lambda s: fnv1a(s, False, 32),
    "FNV-1 lower 30":   lambda s: fnv1(s, True, 30),
    "FNV-1a lower 30":  lambda s: fnv1a(s, True, 30),
}

def collect(root):
    pairs = collections.defaultdict(set)   # kind -> {(name, id)}
    for bank in root.iter("SoundBank"):
        n, i = bank.findtext("ShortName"), bank.get("Id")
        if n and i:
            pairs["SoundBank"].add((n, int(i)))
    for ev in root.iter("Event"):
        n, i = ev.get("Name"), ev.get("Id")
        if n and i:
            pairs["Event"].add((n, int(i)))
    for tag, kind in (("GameParameter", "GameParameter"),
                      ("State", "State"), ("StateGroup", "StateGroup"),
                      ("Switch", "Switch"), ("SwitchGroup", "SwitchGroup"),
                      ("Trigger", "Trigger"), ("AuxBus", "AuxBus"), ("Bus", "Bus"),
                      ("ActionSetStateEntry", "ActionSetStateEntry"),
                      ("ActionTriggerEntry", "ActionTriggerEntry"),
                      ("DialogueEvent", "DialogueEvent")):
        for e in root.iter(tag):
            n, i = e.get("Name"), e.get("Id")
            if n and i:
                pairs[kind].add((n, int(i)))
    for f in root.iter("File"):
        i = f.get("Id")
        sn = f.findtext("ShortName")
        if i and sn:
            pairs["File(media)"].add((sn, int(i)))
    return pairs

def main():
    root = ET.parse(XML).getroot()
    pairs = collect(root)
    rows = []
    for kind in sorted(pairs):
        data = sorted(pairs[kind])
        # dedupe by name: a name must map to one id
        by_name = collections.defaultdict(set)
        for n, i in data:
            by_name[n].add(i)
        multi = sum(1 for n in by_name if len(by_name[n]) > 1)
        res = {a: sum(1 for n, i in data if f(n) == i) for a, f in ALGOS.items()}
        rows.append((kind, len(data), len(by_name), multi, res))

    w = max(len(r[0]) for r in rows) + 2
    hdr = f"{'kind':<{w}}{'pairs':>7}{'names':>7}{'amb':>5}  " + "  ".join(f"{a:>16}" for a in ALGOS)
    print(hdr)
    for kind, n, nn, multi, res in rows:
        print(f"{kind:<{w}}{n:>7}{nn:>7}{multi:>5}  " +
              "  ".join(f"{res[a]:>16}" for a in ALGOS))

    # sanity: known Wwise value
    assert fnv1("play_hello", True) == fnv1("Play_Hello", True)

    if "--dump" in sys.argv:
        out = sys.argv[sys.argv.index("--dump") + 1]
        idx = {k: {n: i for n, i in sorted(v)} for k, v in pairs.items()}
        idx["_media_ids"] = sorted({i for _, i in pairs["File(media)"]})
        json.dump(idx, open(out, "w"), indent=1)
        print("dumped", out, {k: len(v) for k, v in idx.items() if k != "_media_ids"},
              "media_ids:", len(idx["_media_ids"]))

main()
