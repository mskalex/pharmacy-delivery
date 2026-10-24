#!/usr/bin/env python3
"""
Markdown summary of a JUnit XML test report (for the GitHub Actions job summary or any terminal):

    python3 scripts/junit-summary.py "Unit tests" test-results/junit-unit.xml [coverage-search-dir]

Prints totals, failed tests with their messages and, when a coverage.cobertura.xml is found under the optional
directory, line coverage per package. Standard library only.
"""
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def main():
    if len(sys.argv) < 3:
        raise SystemExit(__doc__)
    title, report = sys.argv[1], Path(sys.argv[2])
    print(f"## {title}\n")
    if not report.exists():
        print(f"No report found at `{report}` (the step before failed to produce it).\n")
        return

    root = ET.parse(report).getroot()
    suites = [root] if root.tag == "testsuite" else root.findall("testsuite")
    total = sum(int(s.get("tests", 0)) for s in suites)
    failures = sum(int(s.get("failures", 0)) + int(s.get("errors", 0)) for s in suites)
    skipped = sum(int(s.get("skipped", 0)) for s in suites)
    seconds = sum(float(s.get("time", 0)) for s in suites)
    status = "✅ passed" if failures == 0 else "❌ failed"
    print(f"{status} — **{total - failures - skipped}** passed, **{failures}** failed, **{skipped}** skipped, {seconds:.1f} s\n")

    failed = [case for s in suites for case in s.iter("testcase") if case.find("failure") is not None or case.find("error") is not None]
    if failed:
        print("| Failed test | Message |\n|---|---|")
        for case in failed:
            node = case.find("failure") if case.find("failure") is not None else case.find("error")
            message = (node.get("message") or "").replace("|", "¦").replace("\n", " ")[:300]
            print(f"| `{case.get('name')}` | {message} |")
        print()

    if len(sys.argv) > 3:
        coverage = next(Path(sys.argv[3]).rglob("coverage.cobertura.xml"), None)
        if coverage:
            cov = ET.parse(coverage).getroot()
            print(f"Line coverage: **{float(cov.get('line-rate', 0)) * 100:.1f}%**\n")
            print("| Package | Lines |\n|---|---|")
            for package in cov.iter("package"):
                print(f"| {package.get('name')} | {float(package.get('line-rate', 0)) * 100:.1f}% |")
            print()


if __name__ == "__main__":
    main()
