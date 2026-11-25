"""Refresh the embedded Forex Factory week; then save EconomicCalendar.js in Classic."""
import json
import os
from datetime import datetime, timedelta
from pathlib import Path
from tempfile import NamedTemporaryFile
from subprocess import check_output
from zoneinfo import ZoneInfo

FEED = "https://nfs.faireconomy.media/ff_calendar_thisweek.json"
NY = ZoneInfo("America/New_York")
TARGET = Path(__file__).resolve().parents[1] / "EconomicCalendar.js"


def snapshot(feed, today):
    sunday = today - timedelta(days=(today.weekday() + 1) % 7)
    saturday = sunday + timedelta(days=6)
    rows = set()
    for event in feed:
        stamp = datetime.fromisoformat(event["date"])
        if stamp.utcoffset() is None:
            raise ValueError("Event without an offset")
        if sunday <= stamp.astimezone(NY).date() <= saturday:
            impact = event["impact"]
            rows.add((stamp.isoformat(), event["country"], impact if impact in {"High", "Medium", "Holiday"} else "Low", event["title"]))
    if not rows:
        raise ValueError("No events for the current New York week; keeping existing snapshot")
    ordered = sorted(rows, key=lambda row: (datetime.fromisoformat(row[0]), row[1:]))
    # A single dated release per line keeps weekly changes easy to review.
    data = "[\n" + "".join("  " + json.dumps(row, ensure_ascii=True) + ",\n" for row in ordered) + "]"
    return (f'const snapshotStart = "{sunday:%Y-%m-%d}";\nconst snapshotEnd = "{saturday:%Y-%m-%d}";\n'
            f'// prettier-ignore\nconst releases = {data};')


if __name__ == "__main__":
    payload = check_output(["curl", "--fail", "--silent", "--show-error", "--location", "--max-time", "20", FEED])
    block = snapshot(json.loads(payload), datetime.now(NY).date())
    before, rest = TARGET.read_text().split("// BEGIN WEEKLY SNAPSHOT\n")
    _, after = rest.split("// END WEEKLY SNAPSHOT")
    with NamedTemporaryFile("w", dir=TARGET.parent, delete=False) as output:
        output.write(before + "// BEGIN WEEKLY SNAPSHOT\n" + block + "\n// END WEEKLY SNAPSHOT" + after)
    os.chmod(output.name, TARGET.stat().st_mode & 0o777)
    os.replace(output.name, TARGET)
