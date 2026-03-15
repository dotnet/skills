#!/usr/bin/env python3
"""parse_trx.py — Parse .trx test result files for pass/fail/skip counts."""

import sys
import json
import xml.etree.ElementTree as ET


def parse_trx(trx_path: str) -> dict:
    """Parse a Visual Studio .trx test results file."""
    try:
        tree = ET.parse(trx_path)
        root = tree.getroot()

        # TRX files use a namespace
        ns = {"t": "http://microsoft.com/schemas/VisualStudio/TeamTest/2010"}

        # Get counters from ResultSummary
        counters = root.find(".//t:ResultSummary/t:Counters", ns)
        if counters is not None:
            return {
                "total": int(counters.get("total", 0)),
                "executed": int(counters.get("executed", 0)),
                "passed": int(counters.get("passed", 0)),
                "failed": int(counters.get("failed", 0)),
                "error": int(counters.get("error", 0)),
                "timeout": int(counters.get("timeout", 0)),
                "aborted": int(counters.get("aborted", 0)),
                "not_executed": int(counters.get("notExecuted", 0)),
                "trx_found": True,
            }

        # Fallback: count individual test results
        results = root.findall(".//t:UnitTestResult", ns)
        passed = sum(1 for r in results if r.get("outcome") == "Passed")
        failed = sum(1 for r in results if r.get("outcome") == "Failed")
        total = len(results)

        return {
            "total": total,
            "executed": total,
            "passed": passed,
            "failed": failed,
            "error": 0,
            "timeout": 0,
            "aborted": 0,
            "not_executed": 0,
            "trx_found": True,
        }

    except FileNotFoundError:
        return {"total": 0, "passed": 0, "failed": 0, "trx_found": False}
    except ET.ParseError as e:
        return {"total": 0, "passed": 0, "failed": 0, "trx_found": False, "parse_error": str(e)}


def main():
    if len(sys.argv) < 2:
        print("Usage: parse_trx.py <results.trx>", file=sys.stderr)
        sys.exit(1)

    result = parse_trx(sys.argv[1])
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
