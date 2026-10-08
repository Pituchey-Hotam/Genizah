# -*- coding: utf-8 -*-
"""Writes `model/thresholds.json`: the censor/leave logits per name that the add-in decides with.

The thresholds come from the classifier's phase 4 (`classifier/scratch/2026-10-04/phase4/
thresholds.json`, `classifier/docs/THRESHOLDS-2026-10-05.md`), which has a pair for every
false-censor tolerance on D7's grid. The tolerance per name is the owner's choice and is passed
here, or `always` to censor every candidate of that name whatever its score. A name given
neither is asked about always, as D2 routes the rare names.

    py -3.13 tools/make_thresholds.py el=0.03 elohim=0.10 eloha=0.05 havaya=always

The thresholds belong to one graph: phase 4 read them off that graph's out-of-fold logits. The
script refuses to write them for any other model than the one `model/manifest.json` names.
"""
import argparse
import hashlib
import json
import pathlib

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parent                      # Genizah/
ROOT = REPO.parent                      # GenizahCensor/
PHASE4 = ROOT / "classifier" / "scratch" / "2026-10-04" / "phase4" / "thresholds.json"
MODEL_DIR = REPO / "model"
# The shipping graph phase 4 scored (THRESHOLDS-2026-10-05.md §6); its thresholds.json doesn't record it.
PHASE4_MODEL_SHA256 = "590e92a925fab246a46a91ab37f17f30d4a4d690aaeeea7d9e6ec7897fb4a3dc"
NAME_ORDER = ("havaya", "elohim", "eloha", "adnut", "tsvaot", "ehye", "shaday", "el")


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("choices", nargs="*", metavar="NAME=TOLERANCE",
                    help="a phase 4 false-censor tolerance for a name, e.g. el=0.03, or NAME=always")
    args = ap.parse_args()
    manifest = json.loads((MODEL_DIR / "manifest.json").read_text(encoding="utf-8"))
    if manifest["model_sha256"] != PHASE4_MODEL_SHA256:
        raise SystemExit(f"model/manifest.json names {manifest['model_sha256'][:8]}…, "
                         f"but phase 4's thresholds belong to {PHASE4_MODEL_SHA256[:8]}…")
    model = MODEL_DIR / manifest["model_file"]
    if model.exists() and hashlib.sha256(model.read_bytes()).hexdigest() != PHASE4_MODEL_SHA256:
        raise SystemExit(f"{model} is not the model manifest.json names")
    p4 = json.loads(PHASE4.read_text(encoding="utf-8"))

    chosen, always = {}, set()
    for c in args.choices:
        name, _, tol = c.partition("=")
        if name not in NAME_ORDER:
            raise SystemExit(f"unknown name {name!r}")
        if name in chosen or name in always:
            raise SystemExit(f"{name} is given twice")
        if tol == "always":
            always.add(name)
            continue
        settings = {s["false_censor_tolerance"]: s for s in p4["names"].get(name, {}).get("settings", [])}
        if float(tol) not in settings:
            raise SystemExit(f"phase 4 has no {name} thresholds at tolerance {tol}; it has {sorted(settings)}")
        s = settings[float(tol)]
        chosen[name] = {"name": name, "false_censor_tolerance": s["false_censor_tolerance"],
                        "censor_at": s["censor_at"], "leave_below": s["leave_below"]}

    out = {
        "about": "Censor at or above censor_at, leave below leave_below, ask in between (raw logits). "
                 "Every name in censor_always is censored whatever its score. A name in neither is always asked about. Written by tools/make_thresholds.py.",
        "model_sha256": PHASE4_MODEL_SHA256,
        "source": "classifier/scratch/2026-10-04/phase4/thresholds.json (classifier/docs/THRESHOLDS-2026-10-05.md)",
        "ask_budget": p4["budget"],
        "temperature": p4["temperature"],
        "names": [chosen[n] for n in NAME_ORDER if n in chosen],
        "censor_always": [n for n in NAME_ORDER if n in always],
        "ask_always": [n for n in NAME_ORDER if n not in chosen and n not in always],
    }
    path = MODEL_DIR / "thresholds.json"
    path.write_text(json.dumps(out, indent=1, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    for e in out["names"]:
        print(f"{e['name']:8} tolerance {e['false_censor_tolerance']:.0%}: censor >= {e['censor_at']:+.4f}, "
              f"leave < {e['leave_below']:+.4f}")
    print("censor always:", ", ".join(out["censor_always"]))
    print("ask always:", ", ".join(out["ask_always"]))


if __name__ == "__main__":
    main()
