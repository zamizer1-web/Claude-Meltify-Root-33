#!/usr/bin/env python3
"""Preflight for the Root 33 design sheets.

Lays every sheet's rows and columns over each other and lists:
  UNFILLED      a required cell that is empty
  BAD_TYPE      a cell whose value does not match its column type
  BROKEN_REF    a reference to a row that does not exist
  UNVERIFIED    a filled cell the row marks as not yet confirmed (with the reason)
  TEST_MISSING  a test named in a 'tests' cell that does not exist in the test project
  TEST_FAILED   a named test that failed or did not run (when a results file is given)
  RULE          a cross-sheet consistency rule that does not hold
  (and SHEET_MISSING, DUP_ID, UNKNOWN_COLUMN)

Every row x column crossing is a checkbox. A cell is checked when it is filled,
well-typed, its references resolve, it is not marked unverified, and (for tests
cells) its tests exist and passed.

Usage:
  python3 -I tools/preflight.py [--target core|plugin|release] [--results test-results.trx]
                                [--allow-missing-tests] [--json]

--allow-missing-tests is the pre-build check: tests named in the sheets are written
by the build itself, so their absence is listed but does not block.

Exit code 0 when nothing blocks the chosen target.
"""
import argparse
import json
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SHEETS_DIR = ROOT / "sheets"
TESTS_DIR = ROOT / "src" / "Root33.Core.Tests"
TARGET_ORDER = ["core", "plugin", "release"]
ID_RE = re.compile(r"^[a-z0-9][a-z0-9._\-]*$")
COLOR_RE = re.compile(r"^#[0-9A-Fa-f]{6}$")


class Report:
    def __init__(self):
        self.issues = []
        self.cells = 0
        self.checked = 0

    def add(self, code, sheet, row, column, message):
        self.issues.append({"code": code, "sheet": sheet, "row": row, "column": column, "message": message})


def counts_for_target(col_spec, target):
    needed = col_spec.get("neededBy", ["core"])
    limit = TARGET_ORDER.index(target)
    return any(TARGET_ORDER.index(t) <= limit for t in needed)


def is_empty(value):
    if value is None:
        return True
    if isinstance(value, str) and (value.strip() == "" or value.strip().upper() in ("TODO", "TBD", "?")):
        return True
    return False


def check_type(value, typ):
    """Return an error message, or None when the value fits the type."""
    if typ == "id":
        return None if isinstance(value, str) and ID_RE.match(value) else "not a valid id slug"
    if typ in ("string", "text"):
        return None if isinstance(value, str) and value.strip() else "not a non-empty string"
    if typ == "int":
        return None if isinstance(value, int) and not isinstance(value, bool) else "not an integer"
    if typ == "number":
        return None if isinstance(value, (int, float)) and not isinstance(value, bool) else "not a number"
    if typ == "bool":
        return None if isinstance(value, bool) else "not true/false"
    if typ.startswith("enum:"):
        allowed = typ[5:].split("|")
        return None if value in allowed else f"not one of {allowed}"
    if typ.startswith("ref:"):
        return None if isinstance(value, str) and value.strip() else "not a row id"
    if typ.startswith("refs:"):
        return None if isinstance(value, list) and all(isinstance(v, str) for v in value) else "not a list of row ids"
    if typ in ("list", "tests"):
        return None if isinstance(value, list) and all(isinstance(v, str) and v.strip() for v in value) else "not a list of strings"
    if typ == "object":
        return None if isinstance(value, dict) else "not an object"
    return f"unknown column type {typ}"


def find_test_names():
    names = set()
    if not TESTS_DIR.exists():
        return names
    for path in TESTS_DIR.rglob("*.cs"):
        if "/obj/" in str(path) or "/bin/" in str(path):
            continue
        for m in re.finditer(r"(?:public|internal)\s+(?:async\s+)?(?:void|Task)\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(", path.read_text(encoding="utf-8")):
            names.add(m.group(1))
    return names


def read_test_results(trx_path):
    """Map test method name -> outcome from a .trx file."""
    outcomes = {}
    tree = ET.parse(trx_path)
    ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}
    for res in tree.getroot().iterfind(".//t:UnitTestResult", ns):
        full = res.get("testName", "")
        name = full.split("(")[0].split(".")[-1]
        outcome = res.get("outcome", "")
        if outcomes.get(name) != "Failed":
            outcomes[name] = outcome
    return outcomes


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--target", choices=TARGET_ORDER, default="core")
    ap.add_argument("--results", help="a .trx test results file to require named tests to have passed")
    ap.add_argument("--allow-missing-tests", action="store_true", help="pre-build check: tests the build will write do not block")
    ap.add_argument("--json", action="store_true")
    args = ap.parse_args()

    schema = json.loads((SHEETS_DIR / "_schema.json").read_text(encoding="utf-8"))
    sheets_spec = schema["sheets"]
    rep = Report()
    data = {}

    for name, spec in sheets_spec.items():
        path = SHEETS_DIR / spec["file"]
        if not path.exists():
            rep.add("SHEET_MISSING", name, None, None, f"{spec['file']} does not exist")
            data[name] = []
            continue
        try:
            doc = json.loads(path.read_text(encoding="utf-8"))
        except json.JSONDecodeError as e:
            rep.add("BAD_TYPE", name, None, None, f"invalid JSON: {e}")
            data[name] = []
            continue
        data[name] = doc.get("rows", []) if isinstance(doc, dict) else []

    ids = {name: {r.get("id") for r in rows if isinstance(r, dict)} for name, rows in data.items()}
    all_ids = set().union(*ids.values()) if ids else set()
    test_names = find_test_names()
    outcomes = read_test_results(args.results) if args.results else None

    # Cell-by-cell pass
    for name, spec in sheets_spec.items():
        cols = spec["columns"]
        seen = set()
        for idx, row in enumerate(data[name]):
            if not isinstance(row, dict):
                rep.add("BAD_TYPE", name, f"#{idx}", None, "row is not an object")
                continue
            rid = row.get("id", f"#{idx}")
            if rid in seen:
                rep.add("DUP_ID", name, rid, "id", "id used twice")
            seen.add(rid)
            unverified = row.get("_unverified", {}) or {}
            for key in row:
                if key not in cols and not key.startswith("_"):
                    rep.add("UNKNOWN_COLUMN", name, rid, key, "column not in the schema (typo?)")
            for key in unverified:
                if key not in cols:
                    rep.add("UNKNOWN_COLUMN", name, rid, key, "_unverified names a column not in the schema")
            for col, cspec in cols.items():
                if not counts_for_target(cspec, args.target):
                    continue
                typ = cspec["type"]
                value = row.get(col)
                required = cspec.get("required", False)
                if is_empty(value) or (isinstance(value, list) and not value and required):
                    if required:
                        rep.cells += 1
                        rep.add("UNFILLED", name, rid, col, "required cell is empty")
                    continue
                rep.cells += 1
                ok = True
                err = check_type(value, typ)
                if err:
                    rep.add("BAD_TYPE", name, rid, col, err)
                    ok = False
                elif typ.startswith("ref:") or typ.startswith("refs:"):
                    target_sheet = typ.split(":", 1)[1]
                    for ref in ([value] if typ.startswith("ref:") else value):
                        if ref not in ids.get(target_sheet, set()):
                            rep.add("BROKEN_REF", name, rid, col, f"'{ref}' is not a row in {target_sheet}")
                            ok = False
                elif typ == "tests":
                    for t in value:
                        if t not in test_names:
                            rep.add("TEST_MISSING", name, rid, col, f"test '{t}' not found in src/Root33.Core.Tests")
                            ok = False
                        elif outcomes is not None and outcomes.get(t) != "Passed":
                            rep.add("TEST_FAILED", name, rid, col, f"test '{t}' outcome: {outcomes.get(t, 'not run')}")
                            ok = False
                if col in unverified:
                    rep.add("UNVERIFIED", name, rid, col, str(unverified[col]))
                    ok = False
                if ok:
                    rep.checked += 1

    # Cross-sheet rules
    def rows(n):
        return [r for r in data.get(n, []) if isinstance(r, dict)]

    def by_id(n):
        return {r.get("id"): r for r in rows(n)}

    chars, wins, plans, rules = by_id("characters"), by_id("win_conditions"), by_id("bot_plans"), by_id("bot_rules")
    engines, tuning = by_id("engines"), by_id("tuning")
    char_ids = set(chars)

    for cid, c in chars.items():
        w = wins.get(c.get("winCondition"))
        if w and w.get("character") != cid:
            rep.add("RULE", "characters", cid, "winCondition", f"win condition '{w.get('id')}' belongs to '{w.get('character')}'")
        p = plans.get(c.get("botPlan"))
        if p and p.get("character") != cid:
            rep.add("RULE", "characters", cid, "botPlan", f"bot plan '{p.get('id')}' belongs to '{p.get('character')}'")
        if c.get("v1") is True:
            e = engines.get(c.get("engine"))
            if e and e.get("dlc") != "base":
                rep.add("RULE", "characters", cid, "v1", f"first-version character rides on '{e.get('id')}', which needs DLC '{e.get('dlc')}'")
        if c.get("color") and not COLOR_RE.match(str(c.get("color"))):
            rep.add("BAD_TYPE", "characters", cid, "color", "not #RRGGBB")
        for rv in c.get("rivals") or []:
            if rv == cid:
                rep.add("RULE", "characters", cid, "rivals", "a character cannot be its own rival")

    for cid in char_ids:
        n_w = sum(1 for w in rows("win_conditions") if w.get("character") == cid)
        n_p = sum(1 for p in rows("bot_plans") if p.get("character") == cid)
        if n_w != 1:
            rep.add("RULE", "win_conditions", cid, "character", f"character has {n_w} win conditions (needs exactly 1)")
        if n_p != 1:
            rep.add("RULE", "bot_plans", cid, "character", f"character has {n_p} bot plans (needs exactly 1)")
        bk = [b for b in rows("barks") if b.get("character") == cid]
        if len(bk) < 3 or sum(1 for b in bk if b.get("tier") == "safe") < 2:
            rep.add("RULE", "barks", cid, "character", f"character has {len(bk)} barks ({sum(1 for b in bk if b.get('tier') == 'safe')} safe); needs 3+ with 2+ safe")
        if chars[cid].get("v1") is True:
            n_r = sum(1 for r in rows("reskins") if r.get("character") == cid)
            if n_r < 3:
                rep.add("RULE", "reskins", cid, "character", f"first-version character has {n_r} reskins; needs 3+")

    for wid, w in wins.items():
        if w.get("kind") == "countdown" and is_empty(w.get("confirmTest")):
            rep.add("RULE", "win_conditions", wid, "confirmTest", "countdown wins need a confirm test")
        for key in (w.get("params") or {}):
            if key not in tuning:
                rep.add("BROKEN_REF", "win_conditions", wid, "params", f"param '{key}' is not a row in tuning")

    for tid, t in tuning.items():
        for u in t.get("usedBy") or []:
            if u not in all_ids:
                rep.add("BROKEN_REF", "tuning", tid, "usedBy", f"'{u}' is not a row id in any sheet")

    referenced_rules = set()
    for pid, p in plans.items():
        listed = p.get("rules") or []
        for pos, rid in enumerate(listed, start=1):
            referenced_rules.add(rid)
            r = rules.get(rid)
            if not r:
                continue
            if r.get("plan") != pid:
                rep.add("RULE", "bot_plans", pid, "rules", f"rule '{rid}' belongs to plan '{r.get('plan')}'")
            if r.get("order") != pos:
                rep.add("RULE", "bot_rules", rid, "order", f"order {r.get('order')} but listed at position {pos} in '{pid}'")
    for rid in rules:
        if rid not in referenced_rules:
            rep.add("RULE", "bot_rules", rid, "plan", "rule is not listed in its plan")

    barks = by_id("barks")
    for bid, b in barks.items():
        if b.get("original") is not True:
            rep.add("RULE", "barks", bid, "original", "every line must be original writing")
        if b.get("tier") == "finished":
            sib = barks.get(b.get("safeSibling"))
            if not sib:
                rep.add("RULE", "barks", bid, "safeSibling", "finished-tier lines need a safe sibling")
            elif sib.get("tier") != "safe" or sib.get("character") != b.get("character"):
                rep.add("RULE", "barks", bid, "safeSibling", "safe sibling must be a safe line of the same character")

    for eid, ev in by_id("story_events").items():
        if ev.get("kind") == "spawn" and is_empty(ev.get("spawn")):
            rep.add("RULE", "story_events", eid, "spawn", "spawn events must name the spawn")

    acts = sorted(rows("acts"), key=lambda a: a.get("number", 0))
    for i, a in enumerate(acts, start=1):
        if a.get("number") != i:
            rep.add("RULE", "acts", a.get("id"), "number", f"acts must be numbered 1..n; expected {i}")
        last = i == len(acts)
        if last and (a.get("endsAtVp") != 0 or a.get("endsAfterRound") != 0):
            rep.add("RULE", "acts", a.get("id"), "endsAtVp", "the last act never ends early (use 0)")
        if not last and not (isinstance(a.get("endsAtVp"), int) and a.get("endsAtVp") > 0):
            rep.add("RULE", "acts", a.get("id"), "endsAtVp", "earlier acts need an end")

    autumn = rows("map_autumn")
    if data.get("map_autumn") is not None and len(autumn) != 12:
        rep.add("RULE", "map_autumn", None, None, f"Autumn has 12 clearings; sheet has {len(autumn)}")
    names = [a.get("continentName") for a in autumn]
    for n in {n for n in names if names.count(n) > 1}:
        rep.add("RULE", "map_autumn", None, "continentName", f"'{n}' is used twice")
    role_ids = set(by_id("map_roles"))
    for sid, s in by_id("spawns").items():
        p = s.get("placeAt", "")
        if isinstance(p, str) and p and p not in role_ids and not p.startswith("rule:"):
            rep.add("BROKEN_REF", "spawns", sid, "placeAt", f"'{p}' is not a map role (or start it with 'rule:')")

    for sheet in ("state", "overlays"):
        for oid, o in by_id(sheet).items():
            if o.get("owner") not in char_ids | {"global"}:
                rep.add("BROKEN_REF", sheet, oid, "owner", f"'{o.get('owner')}' is not a character or 'global'")

    for sid, s in by_id("systems").items():
        for sh in s.get("sheets") or []:
            if sh not in sheets_spec:
                rep.add("BROKEN_REF", "systems", sid, "sheets", f"'{sh}' is not a sheet")

    if TARGET_ORDER.index(args.target) >= TARGET_ORDER.index("plugin"):
        for aid, a in by_id("assets").items():
            if a.get("source") == "made-for-mod" and a.get("sourcePath") and not (ROOT / a["sourcePath"]).exists():
                rep.add("RULE", "assets", aid, "sourcePath", f"{a['sourcePath']} is not in the repository")

    # Output
    pending = {"TEST_MISSING"} if args.allow_missing_tests else set()
    blocking = [i for i in rep.issues if i["code"] not in pending]
    waiting = [i for i in rep.issues if i["code"] in pending]
    summary = {
        "target": args.target,
        "cells": rep.cells,
        "checked": rep.checked,
        "issues": len(blocking),
        "pendingTests": len(waiting),
        "byCode": {},
    }
    for i in blocking:
        summary["byCode"][i["code"]] = summary["byCode"].get(i["code"], 0) + 1

    if args.json:
        print(json.dumps({"summary": summary, "issues": blocking, "pending": waiting}, indent=1))
    else:
        print(f"Preflight for target '{args.target}': {rep.checked}/{rep.cells} cells checked, {len(blocking)} blocking issues")
        if waiting:
            print(f"  ({len(waiting)} tests named but not written yet; the build writes them)")
        for code, n in sorted(summary["byCode"].items()):
            print(f"  {code}: {n}")
        current = None
        for i in sorted(blocking, key=lambda x: (x["sheet"] or "", str(x["row"]), x["code"])):
            if i["sheet"] != current:
                current = i["sheet"]
                print(f"\n[{current}]")
            loc = f"{i['row']}.{i['column']}" if i["column"] else str(i["row"])
            print(f"  {i['code']:<14} {loc}: {i['message']}")
    return 0 if not blocking else 1


if __name__ == "__main__":
    sys.exit(main())
