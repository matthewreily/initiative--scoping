#!/usr/bin/env python3
"""Merge Cobertura reports from all test projects and fail if line coverage is below the threshold.

Usage: coverage-check.py <results-dir> [min-percent]
Lines are keyed by (file, line) so a line hit by any test project counts once.
"""
import glob
import sys
import xml.etree.ElementTree as ET

results_dir = sys.argv[1]
threshold = float(sys.argv[2]) if len(sys.argv) > 2 else 80.0

lines: dict[tuple[str, str], bool] = {}
per_assembly: dict[str, dict[tuple[str, str], bool]] = {}
reports = glob.glob(f"{results_dir}/**/coverage.cobertura.xml", recursive=True)
if not reports:
    print(f"No coverage reports found under {results_dir}", file=sys.stderr)
    sys.exit(2)

for report in reports:
    for package in ET.parse(report).getroot().iter("package"):
        assembly = per_assembly.setdefault(package.get("name", "?"), {})
        for cls in package.iter("class"):
            filename = cls.get("filename", "")
            for line in cls.iter("line"):
                key = (filename, line.get("number", ""))
                hit = int(line.get("hits", "0")) > 0
                lines[key] = lines.get(key, False) or hit
                assembly[key] = assembly.get(key, False) or hit


def pct(table: dict[tuple[str, str], bool]) -> float:
    return 100.0 * sum(table.values()) / len(table) if table else 0.0


for name, table in sorted(per_assembly.items()):
    print(f"{name:45s} {pct(table):6.1f}%  ({sum(table.values())}/{len(table)} lines)")

total = pct(lines)
print(f"{'TOTAL':45s} {total:6.1f}%  ({sum(lines.values())}/{len(lines)} lines)  threshold {threshold:.1f}%")
if total < threshold:
    print(f"::error::Line coverage {total:.1f}% is below the required {threshold:.1f}%")
    sys.exit(1)
