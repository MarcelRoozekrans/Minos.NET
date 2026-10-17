#!/usr/bin/env python3
"""Builds the Banking77 sample datasets for Minos.NET.Evaluation.

Reads source/test-subset.csv and urgency-labels.csv and writes the two JSONL files next to this script. With
--download it first refetches the subset from the pinned commit of PolyAI-LDN/task-specific-datasets. The output is
byte-stable: fixed key order, UTF-8 without a BOM, LF line endings. Standard library only.
"""
import argparse
import csv
import hashlib
import io
import json
import sys
import urllib.request
from pathlib import Path

HERE = Path(__file__).resolve().parent

COMMIT = "57ec275d8078af65b7731c2a98be812d844a6d6b"
SOURCE_URL = (
    "https://raw.githubusercontent.com/PolyAI-LDN/task-specific-datasets/"
    f"{COMMIT}/banking_data/test.csv"
)
SUBSET_NAME = "test-subset.csv"
MAIN_FILE = "banking77-minos-v1.jsonl"
URGENCY_FILE = "banking77-urgency-v1.jsonl"
HEADER = ["text", "category"]

# The order is part of the format: it is the order of the options of the intent question.
INTENTS = [
    ("card_arrival", "The customer asks where a card they ordered is, or says it has not arrived."),
    ("card_delivery_estimate", "The customer asks how long delivery of a card takes, or when it will come."),
    ("card_linking", "The customer wants to link a card to the app or to their account."),
    ("card_not_working", "The customer has a card that does not work."),
    ("lost_or_stolen_card", "The customer has lost a card or had it stolen."),
    ("declined_card_payment", "A card payment was declined or rejected."),
    ("pending_card_payment", "A card payment still shows as pending."),
    ("card_payment_fee_charged", "The customer was charged a fee on a card payment."),
    ("activate_my_card", "The customer wants to activate a card."),
    ("change_pin", "The customer wants to change or set the PIN of a card."),
]
CARD_NOT_ARRIVED = {"card_arrival", "card_delivery_estimate"}
ABOUT_A_PAYMENT = {"declined_card_payment", "pending_card_payment", "card_payment_fee_charged"}

URGENCY_PER_INTENT = 6
URGENCY_LEVELS = ["Routine", "Needs action soon", "Urgent: money or card security at risk"]
URGENCY_INSTRUCTIONS = (
    "How urgent is this customer message? Routine means a question or a request for information, with nothing at risk. "
    "Needs action soon means something is not working or is pending, and the customer is blocked. "
    "Urgent means money or card security is at risk, as with a lost or stolen card, an unknown charge or missing money."
)


def dumps(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def read_subset(source_dir):
    with open(Path(source_dir) / SUBSET_NAME, encoding="utf-8", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        if header != HEADER:
            sys.exit(f"{SUBSET_NAME}: expected the header {','.join(HEADER)}.")
        rows = [(text, category) for text, category in reader]
    known = {name for name, _ in INTENTS}
    for text, category in rows:
        if category not in known:
            sys.exit(f"{SUBSET_NAME}: unknown intent {category!r}.")
    return rows


def sha256(text):
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def read_labels(path):
    with open(path, encoding="utf-8", newline="") as f:
        reader = csv.DictReader(f)
        if reader.fieldnames != ["text_sha256", "urgency"]:
            sys.exit(f"{path}: expected the columns text_sha256,urgency.")
        return {row["text_sha256"]: int(row["urgency"]) for row in reader}


def intent_question():
    return {
        "type": "choice",
        "instructions": "Which topic is this customer message about?",
        "criteria": {name: {"description": description} for name, description in INTENTS},
    }


def noul_question(instructions, when_true, when_false):
    return {
        "type": "noul",
        "instructions": instructions,
        "criteria": {"true": when_true, "false": when_false},
    }


def main_questions():
    return {
        "intent": intent_question(),
        "card_not_arrived": noul_question(
            "Is the customer waiting for a card that has not arrived yet, or asking when it will arrive?",
            "The message is about a card that is on its way or late.",
            "The message is about something else.",
        ),
        "about_a_payment": noul_question(
            "Is the message about a card payment that was declined, is still pending, or carried a fee?",
            "The message is about one specific card payment.",
            "The message is not about a card payment.",
        ),
    }


def urgency_questions():
    return {"urgency": {"type": "score", "instructions": URGENCY_INSTRUCTIONS, "criteria": URGENCY_LEVELS}}


def item(item_id, text, questions, expected, source):
    return {"id": item_id, "state": text, "questions": questions, "expected": expected, "source": source, "version": 1}


def numbered(rows):
    """Yields (row, nn, text, intent): row is the 1-based data row of the subset, nn the 1-based place within the intent."""
    seen = {}
    for row, (text, intent) in enumerate(rows, start=1):
        seen[intent] = seen.get(intent, 0) + 1
        yield row, seen[intent], text, intent


def write_lines(path, items):
    data = "".join(dumps(i) + "\n" for i in items).encode("utf-8")
    Path(path).write_bytes(data)


def urgency_picks(rows):
    """The URGENCY_PER_INTENT items of each intent with the lowest sha256 of their text, in file order."""
    by_intent = {}
    for entry in numbered(rows):
        by_intent.setdefault(entry[3], []).append(entry)
    picks = []
    for name, _ in INTENTS:
        entries = by_intent.get(name, [])
        lowest = sorted(entries, key=lambda e: sha256(e[2]))[:URGENCY_PER_INTENT]
        picks.extend(sorted(lowest, key=lambda e: e[1]))
    return picks


def build(source_dir, labels_path, out_dir):
    rows = read_subset(source_dir)
    labels = read_labels(labels_path)
    main_q = main_questions()
    urgency_q = urgency_questions()
    main_items = []
    for row, nn, text, intent in numbered(rows):
        expected = {
            "intent": intent,
            "card_not_arrived": intent in CARD_NOT_ARRIVED,
            "about_a_payment": intent in ABOUT_A_PAYMENT,
        }
        main_items.append(item(f"b77-{intent}-{nn:02d}", text, main_q, expected, f"banking77/test#{row}"))

    urgency_items = []
    missing = []
    used = set()
    for row, nn, text, intent in urgency_picks(rows):
        digest = sha256(text)
        if digest not in labels:
            missing.append(f"{digest} b77-{intent}-{nn:02d} {text}")
            continue
        used.add(digest)
        urgency_items.append(
            item(f"b77-{intent}-{nn:02d}", text, urgency_q, {"urgency": labels[digest]}, f"banking77/test#{row}")
        )
    if missing:
        sys.exit("No urgency label for:\n" + "\n".join(missing))
    unused = sorted(set(labels) - used)
    if unused:
        sys.exit("Unused urgency labels:\n" + "\n".join(unused))

    out = Path(out_dir)
    write_lines(out / MAIN_FILE, main_items)
    write_lines(out / URGENCY_FILE, urgency_items)
    return len(main_items), len(urgency_items)


def download(source_dir):
    with urllib.request.urlopen(SOURCE_URL, timeout=60) as response:
        text = response.read().decode("utf-8")
    reader = csv.reader(io.StringIO(text, newline=""))
    header = next(reader)
    if header != HEADER:
        sys.exit(f"The download has the header {header!r}, not {HEADER!r}.")
    known = {name for name, _ in INTENTS}
    kept = [row for row in reader if row and row[1] in known]
    target = Path(source_dir) / SUBSET_NAME
    target.parent.mkdir(parents=True, exist_ok=True)
    with open(target, "w", encoding="utf-8", newline="") as f:
        writer = csv.writer(f, lineterminator="\n")
        writer.writerow(HEADER)
        writer.writerows(kept)
    return len(kept)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("--download", action="store_true", help=f"refetch {SUBSET_NAME} from the pinned commit first")
    args = parser.parse_args(argv)
    source_dir = HERE / "source"
    if args.download:
        print(f"Downloaded {download(source_dir)} rows to {source_dir / SUBSET_NAME}")
    main_count, urgency_count = build(source_dir, HERE / "urgency-labels.csv", HERE)
    print(f"Wrote {main_count} items to {MAIN_FILE} and {urgency_count} to {URGENCY_FILE}")


if __name__ == "__main__":
    main()
