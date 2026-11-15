"""Parse pdftotext -layout output of ERG 2024 into compact JSON for Guardrail."""
import json, re, sys

src, dst = sys.argv[1], sys.argv[2]
pages = open(src, encoding="utf-8").read().split("\f")

def clean(s):
    s = s.replace("’", "'").replace("‘", "'").replace("“", '"').replace("”", '"')
    return re.sub(r"\s+", " ", s).strip()

FOOT = re.compile(r"^\s*(Page \d+|ERG 2024|ERG 2024\s+Page \d+|Page \d+\s+ERG 2024)\s*$")

# ---------- Yellow pages: ID -> guide -> name ----------
ENTRY = re.compile(r"^(\d{4}|— —)\s+(\d{3}P?)\s+(.+)$")
first_blue = next(i for i, p in enumerate(pages) if re.search(r"Name of Material\s+Guide\s+ID", p))
materials = []
for pi in range(30, first_blue):
    lines = pages[pi].split("\n")
    hdr = next((i for i, l in enumerate(lines) if l.lstrip().startswith("ID Guide")), None)
    if hdr is None:
        continue
    h = lines[hdr]
    split = h.index("ID", h.index("Name of Material"))
    cols = [[], []]
    for l in lines[hdr + 2:]:
        if FOOT.match(l) or "Page " in l and l.strip().startswith("Page"):
            continue
        cols[0].append(l[:split]); cols[1].append(l[split:])
    for col in cols:
        cur = None
        for raw in col:
            s = raw.strip()
            if not s:
                continue
            m = ENTRY.match(s)
            if m:
                cur = {"id": "" if m.group(1) == "— —" else m.group(1), "g": m.group(2), "n": m.group(3)}
                materials.append(cur)
            elif cur is not None and not re.match(r"^(Page \d+|ERG 2024)$", s):
                cur["n"] += " " + s
for m in materials:
    m["n"] = clean(m["n"])

# ---------- Table 1 ----------
D = r"(\d+ m)\s*\((\d+ ft)\)\s+([\d.]+\+? km)\s*\(([\d.]+\+? mi)\)\s+([\d.]+\+? km)\s*\(([\d.]+\+? mi)\)"
DIST = re.compile(D + r"(?:\s+" + D + r"|\s+(Refer to Table 3))?")
T1ID = re.compile(r"^\s*(\d{4})\s+(\d{3}P?)\s+(.*)$")
items = []
for pi in range(291, 335):
    for l in pages[pi].split("\n"):
        if not l.strip() or "TABLE 1" in l or FOOT.match(l):
            continue
        if re.search(r"SMALL SPILLS|From a small|First\s+Then|ISOLATE|in all Directions|DAY\s+NIGHT|Meters\s+\(Feet\)|^\s*—\s*$|means distance", l):
            continue
        if re.match(r"^\s*(ID|No\.)\s", l) or l.strip().startswith("Page "):
            continue
        dm = DIST.search(l)
        dist = None
        if dm:
            g = dm.groups()
            small = [f"{g[0]} ({g[1]})", f"{g[2]} ({g[3]})", f"{g[4]} ({g[5]})"]
            if g[6]:
                large = [f"{g[6]} ({g[7]})", f"{g[8]} ({g[9]})", f"{g[10]} ({g[11]})"]
            elif g[12]:
                large = ["Table 3"]
            else:
                large = []
            dist = {"s": small, "l": large}
            rest = l[:dm.start()]
        else:
            rest = l
        m = T1ID.match(rest)
        if m:
            items.append({"t": "id", "id": m.group(1), "g": m.group(2), "n": m.group(3).strip(), "d": dist})
        elif dist:
            items.append({"t": "d", "d": dist})
        elif items:
            prev = next(x for x in reversed(items) if x["t"] == "id")
            prev["n"] += " " + rest.strip()
for i, x in enumerate(items):
    if x["t"] == "d":
        prev = next((y for y in reversed(items[:i]) if y["t"] == "id"), None)
        if prev and prev["d"] is None:
            prev["d"] = x["d"]
ids = [x for x in items if x["t"] == "id"]
for i, x in enumerate(ids):
    if x["d"] is None:
        for j in list(range(i - 1, -1, -1)) + list(range(i + 1, len(ids))):
            if ids[j]["id"] == x["id"] and ids[j]["d"] is not None:
                x["d"] = ids[j]["d"]; break
missing = [x for x in ids if x["d"] is None]
table1 = [{"id": x["id"], "n": clean(x["n"]), "s": x["d"]["s"], "l": x["d"]["l"]} for x in ids if x["d"]]

# ---------- Table 2 ----------
table2 = []
T2 = re.compile(r"^\s*(\d{4})\s+(\d{3}P?)\s+(.+?)\s{2,}((?:[A-Za-z0-9]+\s?)+)$")
T2b = re.compile(r"^\s*(\d{4})\s+(\d{3}P?)\s+(.+?)\s((?:H[A-Za-z0-9]*|PH3|NH3|SO2|Cl2|Br2|COCl2|SO3|NO2|CO|I2|ClO2|POCl3|SOCl2|SiCl4|TiCl4|NOCl|ClF|BF3|BCl3|HCl|HBr|HF|HCN|H2S|HI)(?:\s+\w+)*)$")
for pi in range(336, 341):
    for l in pages[pi].split("\n"):
        m = T2.match(l) or T2b.match(l)
        if m:
            table2.append({"id": m.group(1), "n": clean(m.group(3)), "gas": clean(m.group(4))})

# ---------- Orange guides ----------
HEAD = {"POTENTIAL HAZARDS", "PUBLIC SAFETY", "EMERGENCY RESPONSE"}
SUB = {"FIRE OR EXPLOSION", "HEALTH", "PROTECTIVE CLOTHING", "EVACUATION", "FIRE", "SPILL OR LEAK", "FIRST AID"}
guides = {}
for pi in range(153, 281):
    lines = [l for l in pages[pi].split("\n")]
    nonblank = [i for i, l in enumerate(lines) if l.strip()]
    body_start = next((i for i in nonblank[:8] if clean(lines[i]) in HEAD or "left blank" in lines[i]), None)
    if body_start is None:
        continue
    hdr = " ".join(clean(lines[i]) for i in nonblank if i < body_start).replace("Guides (orange section)", "")
    n = re.search(r"\b(1[1-7]\d)\b", hdr)
    num = n.group(1) if n else None
    title_parts = [re.sub(r"\bGUIDE\b|\b1[1-7]\d\b", " ", hdr)]
    if not num:
        continue
    g = guides.setdefault(num, {"num": num, "title": "", "lines": []})
    if not g["title"]:
        g["title"] = clean(" ".join(title_parts))
    for l in lines[body_start:]:
        s = l.strip()
        if not s or FOOT.match(l) or re.match(r"^(Page \d+\s+)?ERG 2024(\s+Page \d+)?$", s):
            continue
        s = clean(s)
        if "Emergency Response Assistance Plan" in s or 'consult the shipping paper and/or the "ERAP"' in s:
            continue
        if s in HEAD:
            g["lines"].append(["H", s])
        elif s in SUB or (s.isupper() and len(s) < 40 and not s.startswith("•") and s not in ("CAUTION",)):
            g["lines"].append(["S", s])
        elif s.startswith("•"):
            g["lines"].append(["B", s.lstrip("• ").strip()])
        elif re.match(r"^(Small|Large|Immediate|Fire Involving|CARGO|TIRE|Fire|Spill|Small Spill|Large Spill|Fire involving)\b", s) and len(s) < 60 and not s.endswith("."):
            g["lines"].append(["s", s])
        elif g["lines"] and g["lines"][-1][0] in ("B", "T") and (l.startswith("   ") or l.startswith("  ")) and not s.startswith("*"):
            g["lines"][-1][1] += " " + s
        else:
            g["lines"].append(["T", s])

first_aid = []
for l in pages[151].split("\n"):
    s = l.strip()
    if not s or FOOT.match(l) or s == "GENERAL FIRST AID" or re.match(r"^(Page \d+\s+)?ERG 2024(\s+Page \d+)?$", s):
        continue
    if s.startswith("•"):
        first_aid.append(clean(s.lstrip("• ")))
    elif first_aid:
        first_aid[-1] += " " + clean(s)
out = {
    "firstAid": first_aid,
    "edition": "ERG 2024",
    "materials": [[m["id"], m["g"], m["n"]] for m in materials],
    "guides": [guides[k] for k in sorted(guides)],
    "table1": table1,
    "table2": table2,
}
json.dump(out, open(dst, "w", encoding="utf-8"), ensure_ascii=False, separators=(",", ":"))
print("materials", len(materials), "guides", len(guides), "table1", len(table1), "missing", len(missing), "table2", len(table2))
print("missing sample", [(x["id"], x["n"]) for x in missing[:10]])
