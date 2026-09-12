#!/usr/bin/env python3
import json
import sys

# `dotnet list package --vulnerable` can exit zero with vulnerabilities: parse its report.
with open(sys.argv[1], encoding='utf-8') as file:
    report = json.load(file)
findings = []
for project in report.get('projects', []):
    for framework in project.get('frameworks', []):
        for key in ('topLevelPackages', 'transitivePackages'):
            for package in framework.get(key, []):
                for vulnerability in package.get('vulnerabilities', []):
                    if vulnerability.get('severity', '').lower() in ('high', 'critical'):
                        findings.append((package['id'], vulnerability['severity']))
if report.get('problems'):
    raise SystemExit('NuGet audit incomplete; fail closed.')
print('High/critical NuGet findings:', findings)
raise SystemExit(bool(findings))
