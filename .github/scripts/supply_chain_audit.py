#!/usr/bin/env python3
"""Supply-chain advisory audit for the Rust, npm and Python dependencies.

  collect [--dir DIR]           print the advisory findings for a checkout as JSON
  diff --base REF               fail only on findings this checkout has that REF does not
  report [--markdown FILE]      fail on any finding; optionally write a markdown summary

Exit codes: 0 = clean, 1 = advisory findings, 2 = the audit itself failed.

Advisories are published independently of our code, so a PR should not fail because an advisory
landed against a dependency that is already on the base branch. `diff` audits the base in a
temporary git worktree and fails only on newly introduced findings. The scheduled supply-chain
workflow runs `report` and files an issue for everything else.

Requires cargo-deny, npm, uv and pip-audit on PATH.
"""

import argparse
import json
import os
import subprocess
import sys
import tempfile

NPM_DIR = "src/lib-nodejs"
PYTHON_DIR = "src/lib-python"


def run(cmd, cwd, **kwargs):
    return subprocess.run(cmd, cwd=cwd, capture_output=True, text=True, **kwargs)


def collect_cargo(root):
    # cargo-deny prints one JSON diagnostic per line on stderr; deny.toml decides what is an error.
    proc = run(["cargo", "deny", "--format", "json", "check", "advisories"], root)
    findings = []
    for line in proc.stderr.splitlines():
        try:
            diag = json.loads(line)
        except json.JSONDecodeError:
            continue
        fields = diag.get("fields", {})
        if diag.get("type") != "diagnostic" or fields.get("severity") != "error":
            continue
        advisory = fields.get("advisory") or {}
        krate = next((g.get("Krate") for g in fields.get("graphs", []) if g.get("Krate")), {})
        name = advisory.get("package") or krate.get("name", "?")
        advisory_id = advisory.get("id") or f"{fields.get('code', 'error')}:{name}@{krate.get('version', '?')}"
        findings.append({
            "ecosystem": "cargo",
            "id": advisory_id,
            "package": name,
            "version": krate.get("version", "?"),
            "title": advisory.get("title") or fields.get("message", ""),
        })
    if proc.returncode != 0 and not findings:
        raise RuntimeError(f"cargo deny failed without reporting advisories:\n{proc.stderr[-4000:]}")
    return findings


def collect_npm(root):
    proc = run(["npm", "audit", "--json"], os.path.join(root, NPM_DIR), shell=os.name == "nt")
    report = json.loads(proc.stdout or "{}")
    if "error" in report:
        raise RuntimeError(f"npm audit failed: {report['error']}")
    findings = []
    for name, vuln in report.get("vulnerabilities", {}).items():
        for via in vuln.get("via", []):
            # string entries point at another vulnerable package, which is reported on its own
            if isinstance(via, dict):
                findings.append({
                    "ecosystem": "npm",
                    "id": via.get("url") or str(via.get("source")),
                    "package": name,
                    "version": vuln.get("range", "?"),
                    "title": via.get("title", ""),
                })
    return findings


def collect_python(root):
    project = os.path.join(root, PYTHON_DIR)
    export = run(["uv", "export", "--frozen", "--no-hashes", "--no-emit-project"], project)
    if export.returncode != 0:
        raise RuntimeError(f"uv export failed:\n{export.stderr[-4000:]}")
    with tempfile.NamedTemporaryFile("w", suffix=".txt", delete=False) as req:
        req.write(export.stdout)
    try:
        proc = run(["pip-audit", "-r", req.name, "-f", "json", "--progress-spinner", "off"], project)
    finally:
        os.unlink(req.name)
    report = json.loads(proc.stdout or "{}")
    if "dependencies" not in report:
        raise RuntimeError(f"pip-audit failed:\n{proc.stderr[-4000:]}")
    return [
        {"ecosystem": "python", "id": v["id"], "package": d["name"], "version": d.get("version", "?"),
         "title": ", ".join(v.get("aliases", []))}
        for d in report["dependencies"] for v in d.get("vulns", [])
    ]


def collect(root):
    return collect_cargo(root) + collect_npm(root) + collect_python(root)


def key(finding):
    return (finding["ecosystem"], finding["id"], finding["package"])


def describe(finding):
    return f"[{finding['ecosystem']}] {finding['id']} in {finding['package']} {finding['version']}: {finding['title']}"


def cmd_diff(base):
    head = collect(".")
    with tempfile.TemporaryDirectory() as tmp:
        worktree = os.path.join(tmp, "base")
        subprocess.run(["git", "worktree", "add", "--detach", worktree, base], check=True, capture_output=True)
        try:
            base_keys = {key(f) for f in collect(worktree)}
        finally:
            subprocess.run(["git", "worktree", "remove", "--force", worktree], check=False, capture_output=True)

    introduced = [f for f in head if key(f) not in base_keys]
    existing = [f for f in head if key(f) in base_keys]
    for f in existing:
        # already on the base branch: the scheduled supply-chain workflow tracks these
        print(f"::warning::Pre-existing advisory (not introduced here): {describe(f)}")
    for f in introduced:
        print(f"::error::Advisory introduced by this change: {describe(f)}")
    print(f"{len(introduced)} introduced, {len(existing)} pre-existing advisory finding(s).")
    return 1 if introduced else 0


def cmd_report(markdown):
    findings = collect(".")
    for f in findings:
        print(f"::error::{describe(f)}")
    if markdown:
        with open(markdown, "w", encoding="utf-8") as out:
            out.write("The scheduled supply-chain audit found the following advisories on `develop`:\n\n")
            out.write("| Ecosystem | Advisory | Package | Version | Title |\n|---|---|---|---|---|\n")
            for f in findings:
                title = f["title"].replace("|", "\\|")
                out.write(f"| {f['ecosystem']} | {f['id']} | {f['package']} | {f['version']} | {title} |\n")
    print(f"{len(findings)} advisory finding(s).")
    return 1 if findings else 0


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p_collect = sub.add_parser("collect")
    p_collect.add_argument("--dir", default=".")
    p_diff = sub.add_parser("diff")
    p_diff.add_argument("--base", required=True)
    p_report = sub.add_parser("report")
    p_report.add_argument("--markdown")
    args = parser.parse_args()

    if args.command == "collect":
        json.dump(collect(args.dir), sys.stdout, indent=2)
        return 0
    if args.command == "diff":
        return cmd_diff(args.base)
    return cmd_report(args.markdown)


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as ex:  # distinguish a broken audit from advisory findings
        print(f"::error::Supply-chain audit failed to run: {ex}")
        sys.exit(2)
