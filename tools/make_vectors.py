# -*- coding: utf-8 -*-
"""Generates the expected outputs the C# port of the model's input pipeline is tested against.

The Python code is the source of truth for all three pieces the add-in reimplements:

- normalization: `classifier/clf/normalize.py` (`classifier/docs/NORMALIZATION.md`);
- the candidate finder: `dataset/olah/extract/names.py` `UNDASHED_PATTERNS`, scanned the way
  `dataset/olah/label/occurrences.py` `_undashed_pass` scans (longest span wins, `NAME_ORDER`
  breaks ties, spans never overlap). OSHB imports the same patterns, so both datasets and the
  add-in find the same occurrences;
- the window and tokenizer: `berel3.py` `encode_rows` — `normalize(left)[-32:] + target +
  normalize(right)[:32]`, the fit's `tokenizer.json`, `[CLS] … [SEP]` cut to 128 ids, and a
  target mask of every piece whose offsets overlap the target;
- the model's output: the fit's shipping ONNX graph under onnxruntime, one window per run. The
  int8 layers quantize activations with a scale taken over the whole batch, so a window's logit
  depends on what it is batched with; the add-in scores each window alone, and so do these.

Run from the repo root of the add-in (`Genizah/`) with the interpreter that has `tokenizers`,
torch and onnxruntime (`py -3.13`):

    py -3.13 tools/make_vectors.py [--fit DIR] [--no-local]

Writes, committed (this repo is public, so nothing here may contain Olah text):

    model/vocab.txt, model/manifest.json          the tokenizer vocabulary, one piece per line
    Genizah.Core.Tests/testdata/normalization.json  verbatim copy of the classifier's vectors
    Genizah.Core.Tests/testdata/patterns.json       the Python regexes, as strings
    Genizah.Core.Tests/testdata/pipeline.json       raw text -> candidates -> model inputs -> logits

and, git-ignored, under `Genizah.Core.Tests/testdata/local/` (Olah text; never commit):

    windows.jsonl   every training row's model inputs, from the training code itself
    scans.jsonl     every Olah page/footnote and OSHB chapter, with its candidates
"""
import argparse
import hashlib
import json
import pathlib
import sys

HERE = pathlib.Path(__file__).resolve().parent
REPO = HERE.parent                      # Genizah/
ROOT = REPO.parent                      # GenizahCensor/
CLF = ROOT / "classifier"
OLAH = ROOT / "dataset" / "olah"
OSHB = ROOT / "dataset" / "oshb"
BEREL3 = CLF / "scratch" / "2026-09-17" / "berel3"
DEFAULT_FIT = BEREL3 / "modal_out" / "berel3-full-L12-olah+oshb-fit"
TESTDATA = REPO / "Genizah.Core.Tests" / "testdata"
MODEL_DIR = REPO / "model"

sys.path.insert(0, str(CLF))
sys.path.insert(0, str(OLAH))
from clf.normalize import normalize  # noqa: E402
from extract.names import UNDASHED_PATTERNS  # noqa: E402
from label.patterns import NAME_ORDER  # noqa: E402

SIDE = 32
MODEL_FILE = "model.int8-int4emb.onnx"
MAX_LEN = 128

# The tokenizer configuration the C# port implements. A tokenizer.json that differs in any of
# these stops the script: the port would have to change first.
EXPECTED_TOKENIZER = {
    "normalizer": {"type": "Sequence", "normalizers": [
        {"type": "NFD"}, {"type": "Lowercase"}, {"type": "StripAccents"},
        {"type": "Replace", "pattern": {"String": "<foreign>"}, "content": "[UNK]"},
        {"type": "Replace", "pattern": {"Regex": "[^֐-׿\u0000-\u007f\N{ZERO WIDTH NON-JOINER}-‿"
                                                 "₠-₿∀-⋿⅐-↋"
                                                 "ﬀ-ﭏ]+"}, "content": "[UNK]"}]},
    "pre_tokenizer": {"type": "Split", "pattern": {
        "Regex": "\\[UNK\\]|\\w*[א-ת](['\\\"][א-ת]\\w*)?'?|\\w+|\\p{P}|[^\\w\\s]+"},
        "behavior": "Removed", "invert": True},
    "model": {"type": "WordPiece", "unk_token": "[UNK]", "continuing_subword_prefix": "##",
              "max_input_chars_per_word": 100},
    "special": {"[UNK]": 0, "[CLS]": 1, "[SEP]": 2, "[PAD]": 3},
}

# Hand-written cases, each a raw text the add-in could see. They cover what the OSHB sample
# can't: nikud inside words, presentation forms, characters outside the alphabet, and the
# proclitic and suffix edges of the patterns.
HANDWRITTEN = [
    ("plain prose, several names", "ברוך אתה ה' אלהינו מלך העולם, אל שדי ואדני צבאות."),
    ("vocalized text with maqaf", "וַיְדַבֵּ֥ר אֱלֹהִ֖ים אֶל־מֹשֶׁ֑ה וַיֹּ֥אמֶר אֵלָ֖יו אֲנִ֥י יְהוָֽה׃"),
    ("vocalized word containing אל is not split", "בְּנֵי יִשְׂרָאֵל וּבֵית־אֵל"),
    ("proclitic stack", "והאלהים וכשהאלהים ולאל מהאל שהאל כאלהים בשדי"),
    ("bare relative ש does not open a name", "שאל שאלה שאהיה כשאל"),
    ("inflected elohim and eloha", "אלהי אלהיך אלהינו אלהיכם אלהיהם אלהיו אלהיה אלוהות האלוהית אלוהיות"),
    ("el with 1cs possessive, and longer words", "אלי אליו אלה אלהן אלמוני"),
    ("ehye in its frame", "אהיה אשר אהיה"),
    # אַל, אלהּים, the ﭏ ligature alone and with a proclitic, and שּדי.
    ("presentation forms and the אל ligature", "אַל אַלהּים ﭏ בﭏ שּדי"),
    ("latin, digits and symbols", "Genesis 1:1 — אלהים ברא (בראשית א, א) 100% & more"),
    ("emoji and other unknown characters", "אל 😀 אלהים ☺☺ שדי ✡"),
    ("quotes, geresh and gershayim", "ה׳ אלהי ישראל, “אלהים” ‘אל’ רמב״ם אמר"),
    ("dashed forms are already censored, not candidates", "א-להים ה-ל -להינו יקוק אלוקים ד'"),
    ("bidi marks and zero-width characters inside words", "א\N{RIGHT-TO-LEFT MARK}ל\N{ZERO WIDTH SPACE}הים \N{RIGHT-TO-LEFT EMBEDDING}אל\N{POP DIRECTIONAL FORMATTING}"),
    ("tabs, newlines and table-cell marks", "אלהים\tאל\r\nשדי\r\x07צבאות\x0bאדני"),
    ("very long unknown word",
     "אל " + "א" * 120 + " אלהים"),
    ("empty text", ""),
    ("name at the very start and end", "אלהים"),
    ("long context is cut to 32 characters a side",
     "וזאת הברכה אשר ברך משה איש האלהים את בני ישראל לפני מותו ויאמר יהוה מסיני בא וזרח משעיר למו"),
]

# OSHB chapters: public-domain text with every name in scope, including the ambiguous ones
# (שדי as "fields", idol-sense אלהים, Aramaic אלה).
OSHB_SAMPLE = [("Gen", 1), ("Exod", 3), ("Deut", 10), ("Ruth", 1), ("Ps", 68), ("Dan", 2), ("Job", 22)]


def scan(normalized):
    """`_undashed_pass` over one normalized string: [(start, end, name)]."""
    hits = []
    for name in NAME_ORDER:
        for m in UNDASHED_PATTERNS[name].finditer(normalized):
            hits.append((m.start(), m.end(), name))
    hits.sort(key=lambda h: (h[0], -(h[1] - h[0]), NAME_ORDER.index(h[2])))
    taken, out = [], []
    for a, b, name in hits:
        if any(a < y and x < b for x, y in taken):
            continue
        taken.append((a, b))
        out.append((a, b, name))
    return sorted(out)


def encode(tok, left, target, right):
    """`berel3.py` `encode_rows` for one row: (input_ids, target_mask)."""
    lpart, rpart = normalize(left)[-SIDE:], normalize(right)[:SIDE]
    text, lo, hi = lpart + target + rpart, len(lpart), len(lpart) + len(target)
    enc = tok.encode(text)
    ids = enc.ids[:MAX_LEN]
    tgt = [1 if (a < hi and b > lo) else 0 for a, b in enc.offsets[:MAX_LEN]]
    if not any(tgt):
        raise ValueError(f"target lost in {enc.tokens}")
    return ids, tgt


def logit(sess, ids, tgt):
    """The model's output for one window, run alone (batch 1, so no padding)."""
    import numpy as np
    feed = {"input_ids": np.array([ids], np.int64), "attention_mask": np.ones((1, len(ids)), np.float32),
            "target_mask": np.array([tgt], np.float32)}
    return float(sess.run(None, feed)[0][0])


def pipeline_case(tok, sess, case, text):
    n = normalize(text)
    cands = []
    for a, b, name in scan(n):
        ids, tgt = encode(tok, n[:a], n[a:b], n[b:])
        cands.append({"start": a, "end": b, "name": name, "target": n[a:b],
                      "input_ids": ids, "target_mask": tgt, "logit": logit(sess, ids, tgt)})
    return {"case": case, "text": text, "normalized": n, "candidates": cands}


def check_tokenizer(tj):
    got = {"normalizer": tj["normalizer"], "pre_tokenizer": tj["pre_tokenizer"],
           "model": {k: v for k, v in tj["model"].items() if k != "vocab"},
           "special": {k: tj["model"]["vocab"][k] for k in EXPECTED_TOKENIZER["special"]}}
    for k, want in EXPECTED_TOKENIZER.items():
        if got[k] != want:
            sys.exit(f"tokenizer.json {k} differs from what the C# port implements:\n"
                     f"  got  {json.dumps(got[k], ensure_ascii=False)}\n"
                     f"  want {json.dumps(want, ensure_ascii=False)}")
    post = tj["post_processor"]
    if [next(iter(p)) for p in post["single"]] != ["SpecialToken", "Sequence", "SpecialToken"] or \
            post["single"][0]["SpecialToken"]["id"] != "[CLS]" or post["single"][2]["SpecialToken"]["id"] != "[SEP]":
        sys.exit("tokenizer.json post_processor is not [CLS] A [SEP]")
    vocab = tj["model"]["vocab"]
    if sorted(vocab.values()) != list(range(len(vocab))):
        sys.exit("vocabulary ids are not 0..n-1")
    for piece in vocab:
        if "\n" in piece or "\r" in piece:
            sys.exit(f"vocabulary piece {piece!r} can't be written one per line")


def write_json(path, obj):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, ensure_ascii=False, indent=1) + "\n", encoding="utf-8", newline="\n")


def sha256(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for block in iter(lambda: f.read(1 << 20), b""):
            h.update(block)
    return h.hexdigest()


def read_jsonl(path):
    """One record per LF-terminated line. Not `str.splitlines`: the corpora hold characters
    it also splits on (U+001C-U+001E, U+0085, U+2028)."""
    with open(path, encoding="utf-8", newline=chr(10)) as f:
        for line in f:
            if line.strip():
                yield json.loads(line)


def oshb_chapters():
    for path in sorted((OSHB / "corpus").glob("*.jsonl")):
        yield from read_jsonl(path)


def olah_texts():
    for path in sorted((OLAH / "corpus").glob("*.jsonl")):
        for n, rec in enumerate(read_jsonl(path)):
            yield f"{path.stem}:{n}:body", rec.get("body") or ""
            for i, fn in enumerate(rec.get("footnotes") or []):
                yield f"{path.stem}:{n}:fn{i}", fn or ""


def main():
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--fit", type=pathlib.Path, default=DEFAULT_FIT, help="the shipping fit's run directory")
    ap.add_argument("--no-local", action="store_true", help="skip the git-ignored full-data files")
    args = ap.parse_args()

    from tokenizers import Tokenizer
    tj_path = args.fit / "tokenizer.json"
    tj = json.loads(tj_path.read_text(encoding="utf-8"))
    check_tokenizer(tj)
    tok = Tokenizer.from_file(str(tj_path))

    import onnxruntime as ort
    model_path = args.fit / MODEL_FILE
    so = ort.SessionOptions()
    so.intra_op_num_threads = 1
    sess = ort.InferenceSession(str(model_path), so, providers=["CPUExecutionProvider"])

    # model/: the vocabulary and where it came from.
    vocab = sorted(tj["model"]["vocab"].items(), key=lambda kv: kv[1])
    MODEL_DIR.mkdir(exist_ok=True)
    (MODEL_DIR / "vocab.txt").write_text("".join(p + "\n" for p, _ in vocab), encoding="utf-8", newline="\n")
    fit = json.loads((args.fit / "fit.json").read_text(encoding="utf-8"))
    write_json(MODEL_DIR / "manifest.json", {
        "about": "The shipping model's provenance. The .onnx file is not in git; copy it here from "
                 "the fit directory. Regenerate this file and vocab.txt with tools/make_vectors.py.",
        "fit_dir": str(args.fit.relative_to(ROOT)).replace("\\", "/"),
        "model_file": MODEL_FILE,
        "model_sha256": sha256(model_path),
        "tokenizer_sha256": sha256(tj_path),
        "data_sha256": fit["data_sha256"],
        "window": fit["recipe"]["window"], "max_len": MAX_LEN, "side": SIDE,
        "inputs": {"input_ids": "int64 [batch, seq]", "attention_mask": "float32 [batch, seq]",
                   "target_mask": "float32 [batch, seq]"},
        "output": "logits float32 [batch], positive means censor",
        "batching": "one window per run: the int8 layers quantize activations over the whole batch, "
                    "so batch-mates change a window's logit",
        "onnxruntime": ort.__version__,
    })

    # Committed vectors.
    (TESTDATA).mkdir(parents=True, exist_ok=True)
    (TESTDATA / "normalization.json").write_bytes((CLF / "tests" / "vectors" / "normalization.json").read_bytes())
    write_json(TESTDATA / "patterns.json", {
        "about": "dataset/olah/extract/names.py UNDASHED_PATTERNS, as Python compiled them.",
        "name_order": NAME_ORDER,
        "patterns": {name: UNDASHED_PATTERNS[name].pattern for name in NAME_ORDER},
    })
    chapters = {(c["book"], c["chapter"]): c["body"] for c in oshb_chapters()}
    cases = [pipeline_case(tok, sess, case, text) for case, text in HANDWRITTEN]
    cases += [pipeline_case(tok, sess, f"OSHB {b} {c}", chapters[(b, c)]) for b, c in OSHB_SAMPLE]
    write_json(TESTDATA / "pipeline.json", {
        "about": "Raw text -> normalized text -> candidates (normalized offsets) -> model inputs -> "
                 "logits (each window run alone). Generated by tools/make_vectors.py from the Python "
                 "sources of truth.",
        "side": SIDE, "max_len": MAX_LEN, "tokenizer_sha256": sha256(tj_path),
        "model_sha256": sha256(model_path), "onnxruntime": ort.__version__, "cases": cases,
    })
    n_cands = sum(len(c["candidates"]) for c in cases)
    print(f"pipeline.json: {len(cases)} cases, {n_cands} candidates")

    if args.no_local:
        return
    local = TESTDATA / "local"
    local.mkdir(exist_ok=True)

    # Every training row, through the training code's own encoder.
    sys.path.insert(0, str(BEREL3))
    import berel3  # noqa: E402  (imports torch; the encoder must be the one training used)
    from clf.data import load_train
    rows = load_train()
    enc = berel3.encode_rows(rows, tok)
    with open(local / "windows.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for e, (ids, tgt) in zip(rows, enc):
            f.write(json.dumps({"id": e.id, "left": e.left, "target": e.target, "right": e.right,
                                "input_ids": ids, "target_mask": [int(t) for t in tgt]},
                               ensure_ascii=False) + "\n")
    print(f"local/windows.jsonl: {len(rows)} training rows")

    # Every text both datasets were labelled from, scanned.
    n_texts = n_hits = 0
    with open(local / "scans.jsonl", "w", encoding="utf-8", newline="\n") as f:
        for c in oshb_chapters():
            n = normalize(c["body"])
            spans = scan(n)
            f.write(json.dumps({"id": f"oshb:{c['book']}:{c['chapter']}", "text": c["body"],
                                "candidates": [[a, b, name] for a, b, name in spans]}, ensure_ascii=False) + "\n")
            n_texts, n_hits = n_texts + 1, n_hits + len(spans)
        for key, text in olah_texts():
            spans = scan(normalize(text))
            f.write(json.dumps({"id": f"olah:{key}", "text": text,
                                "candidates": [[a, b, name] for a, b, name in spans]}, ensure_ascii=False) + "\n")
            n_texts, n_hits = n_texts + 1, n_hits + len(spans)
    print(f"local/scans.jsonl: {n_texts} texts, {n_hits} candidates")

    # Does scanning running (normalized) text find what OSHB's word-by-word matcher found?
    oshb_found = {}
    for r in read_jsonl(OSHB / "candidates.jsonl"):
        oshb_found.setdefault((r["book"], r["chapter"]), []).append((r["char_offset"], r["char_end"], r["name"]))
    same = differ = 0
    examples = []
    for key, body in chapters.items():
        want = sorted(oshb_found.get(key, []))
        n = normalize(body)
        got = scan(n) if n == body else None
        if got == want:
            same += 1
        else:
            differ += 1
            if len(examples) < 5:
                examples.append((key, "normalize changes the body" if got is None else
                                 sorted(set(got) ^ set(want))[:6]))
    print(f"OSHB running-text scan vs candidates.jsonl: {same} chapters identical, {differ} differ")
    for ex in examples:
        print("  ", ex)


if __name__ == "__main__":
    main()
