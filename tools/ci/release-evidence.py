#!/usr/bin/env python3
"""CI release evidence only. Never builds, scans, publishes or accesses credentials."""
import argparse
import hashlib
import json
import os
import pathlib
import re


def attestation_mode(private, owner_type, enabled):
    if private not in ('true', 'false') or owner_type not in ('User', 'Organization'):
        raise ValueError('Missing or unsupported repository metadata')
    if private == 'true' and owner_type == 'User':
        return 'unavailable'
    # Owner opt-in confirms plan support; private organizations require Enterprise
    # Cloud. No network probe, visibility change, PAT, or fallback on action failure.
    return 'enabled' if enabled.lower() == 'true' else 'not-enabled'


def attestation_record(env):
    mode, outcome = env['ATTESTATION_MODE'], env['ATTESTATION_OUTCOME']
    if mode in ('unavailable', 'not-enabled'):
        if outcome != 'skipped':
            raise ValueError('Unexpected attestation outcome for disabled mode')
        reason = ('GitHub persistence unsupported for user-owned private repositories'
                  if mode == 'unavailable' else 'GitHub attestation not explicitly enabled')
        return {'status': mode, 'provider': 'github', 'reason': reason}
    if mode != 'enabled' or outcome != 'success':
        raise ValueError('Enabled attestation must succeed; failures are not downgraded')
    identifier = env.get('ATTESTATION_ID', '')
    url = env.get('ATTESTATION_URL', '')
    expected = f"https://github.com/{env['GITHUB_REPOSITORY']}/attestations/{identifier}"
    if not identifier.isdecimal() or url != expected:
        raise ValueError('Successful attestation requires its persisted GitHub reference')
    return {'status': 'attested', 'provider': 'github', 'id': identifier, 'url': url}


def component_from(env):
    component = env['COMPONENT']
    if component not in ('api', 'worker', 'web'):
        raise ValueError('Invalid image component')
    return component


def evidence_reference(path):
    return {'path': 'images/' + path.name, 'sha256': hashlib.sha256(path.read_bytes()).hexdigest()}


def complete(root, env):
    component = component_from(env)
    for stage in ('BUILD', 'SCAN', 'PUBLISH', 'SBOM'):
        if env.get(stage + '_OUTCOME') != 'success':
            raise ValueError(f'{stage} must succeed before recording a releasable image')
    image_path = root/f'{component}-image.json'
    image = json.loads(image_path.read_text())
    if image['component'] != component or image['commit'] != env['GITHUB_SHA']:
        raise ValueError('Image component/source mismatch')
    name = f"ghcr.io/{env['GITHUB_REPOSITORY'].split('/')[0].lower()}/weymela-v3-{component}"
    if not re.fullmatch(re.escape(name) + r'@sha256:[a-f0-9]{64}', image['digest']):
        raise ValueError('Invalid published digest')
    scan_path = root/f'{component}-security.json'
    scan = json.loads(scan_path.read_text())
    if scan.get('SchemaVersion') != 2 or not isinstance(scan.get('Results'), list):
        raise ValueError('Missing or invalid Trivy scan evidence')
    for result in scan['Results']:
        for finding in (result.get('Vulnerabilities') or []) + (result.get('Secrets') or []):
            if finding.get('Severity') in ('HIGH', 'CRITICAL'):
                raise ValueError('Blocking Trivy finding cannot be marked passed')
    scan_image_id = scan.get('Metadata', {}).get('ImageID')
    if scan_image_id and scan_image_id != image['imageId']:
        raise ValueError('Scan does not belong to the published image')
    sbom_path = root/f'{component}-sbom.json'
    if json.loads(sbom_path.read_text()).get('bomFormat') != 'CycloneDX':
        raise ValueError('Missing or invalid CycloneDX SBOM')
    image.update({
        'buildStatus': 'passed', 'publishStatus': 'passed',
        'scan': {**evidence_reference(scan_path), 'status': 'passed', 'scanner': 'trivy',
                 'scanners': ['vuln', 'secret'], 'severities': ['HIGH', 'CRITICAL'], 'ignoreUnfixed': False},
        'sbom': {**evidence_reference(sbom_path), 'format': 'CycloneDX'},
        'attestation': attestation_record(env),
    })
    image_path.write_text(json.dumps(image, indent=2) + '\n')
    return image


def sanitized_scan(scan):
    if not isinstance(scan, dict) or scan.get('SchemaVersion') != 2:
        raise ValueError('Invalid scan schema')
    # Trivy's Results has json omitempty: absence can represent no findings,
    # but is never proof that a failed scanner step passed.
    results = scan.get('Results', [])
    if not isinstance(results, list):
        raise ValueError('Invalid scan results')
    safe_results, matches = [], set()
    counts = {'vulnerabilities': {'HIGH': 0, 'CRITICAL': 0}, 'secrets': {'HIGH': 0, 'CRITICAL': 0}}
    for result in results:
        if not isinstance(result, dict):
            raise ValueError('Invalid result')
        safe = {key: result[key] for key in ('Target', 'Class', 'Type') if key in result}
        for kind in ('Vulnerabilities', 'Secrets'):
            findings = result.get(kind)
            if findings is None:
                findings = []
            if not isinstance(findings, list) or any(not isinstance(item, dict) for item in findings):
                raise ValueError('Invalid finding list')
            for item in findings:
                severity = item.get('Severity')
                if not isinstance(severity, str):
                    raise ValueError('Invalid severity')
                if severity in counts[kind.lower()]:
                    counts[kind.lower()][severity] += 1
                if kind == 'Secrets':
                    match = item.get('Match', '')
                    if not isinstance(match, str):
                        raise ValueError('Invalid secret match')
                    if match:
                        matches.add(match)
            if kind == 'Vulnerabilities':
                safe[kind] = findings  # Keep every reported CVE/package/version/fix.
            else:
                safe[kind] = [{key: item[key] for key in ('RuleID', 'Category', 'Severity', 'Title') if key in item}
                              for item in findings]  # Never Match, Code or surrounding source.
        safe_results.append(safe)

    def redact(value):
        if isinstance(value, str):
            for match in sorted(matches, key=len, reverse=True):
                value = value.replace(match, '[REDACTED]')
        elif isinstance(value, list):
            value = [redact(item) for item in value]
        elif isinstance(value, dict):
            value = {redact(key): redact(item) for key, item in value.items()}
        return value

    # Deliberately exclude Metadata.ImageConfig: it can contain image env secrets.
    metadata = scan.get('Metadata')
    operating_system = metadata.get('OS') if isinstance(metadata, dict) else None
    safe_scan = {'SchemaVersion': 2, 'Results': safe_results}
    if isinstance(operating_system, dict):
        safe_scan['OS'] = {key: operating_system[key] for key in ('Family', 'Name', 'Eosl') if key in operating_system}
    return redact(safe_scan), counts


def diagnostics(root, output, env):
    component = component_from(env)
    record = {'component': component, 'commit': env['GITHUB_SHA'],
              'outcomes': {stage.lower(): env.get(stage + '_OUTCOME', '')
                           for stage in ('BUILD', 'SCAN', 'PUBLISH', 'ATTESTATION')}}
    image_path = root/f'{component}-image.json'
    if image_path.exists():
        image = json.loads(image_path.read_text())
        record['publishedImage'] = {key: image[key] for key in ('image', 'digest', 'commit')}
    bases = root/f'{component}-bases.txt'
    if bases.exists():
        record['baseImages'] = bases.read_text().splitlines()
    report = root/f'{component}-security.json'
    record['scanReportPath'] = f'.artifacts/release/{component}-security.json'
    try:
        record['trivy'], record['findingCounts'] = sanitized_scan(json.loads(report.read_text()))
        record['scanReportStatus'] = 'present'
        if any(sum(counts.values()) for counts in record['findingCounts'].values()):
            record['scanAssessment'] = 'blocking-findings-present'
        elif env.get('SCAN_OUTCOME') != 'success':
            record['scanAssessment'] = 'failure-without-retained-blocking-findings'
            record['scanReportError'] = 'No blocking finding retained; inspect scanner/job error. Not a clean scan.'
        else:
            record['scanAssessment'] = 'no-blocking-findings-in-report'
    except FileNotFoundError:
        record['scanReportStatus'] = 'missing'
        record['scanReportError'] = 'Report missing; consult complete job log. Not a clean scan.'
    except ValueError:
        record['scanReportStatus'] = 'invalid'
        record['scanReportError'] = 'Report invalid or incomplete; consult complete job log. Not a clean scan.'
    except OSError:
        record['scanReportStatus'] = 'unreadable'
        record['scanReportError'] = 'Report unreadable; consult complete job log. Not a clean scan.'
    output.mkdir(parents=True, exist_ok=True)
    (output/f'{component}-diagnostics.json').write_text(json.dumps(record, indent=2) + '\n')
    return record


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('operation', choices=('policy', 'complete', 'diagnostics'))
    args = parser.parse_args()
    if args.operation == 'policy':
        mode = attestation_mode(os.environ['REPOSITORY_PRIVATE'], os.environ['REPOSITORY_OWNER_TYPE'], os.environ.get('ATTESTATION_ENABLED', ''))
        with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
            output.write('mode=' + mode + '\n')
        print('GitHub attestation mode: ' + mode)
    elif args.operation == 'complete':
        complete(pathlib.Path('.artifacts/release'), os.environ)
    else:
        record = diagnostics(pathlib.Path('.artifacts/release'), pathlib.Path('.artifacts/diagnostics'), os.environ)
        print(f"Security report: {record['scanReportStatus']}. See artifact weymela-diagnostics-{record['component']}.")
        if 'findingCounts' in record:
            counts = record['findingCounts']
            print(f"Retained HIGH/CRITICAL vulnerabilities: {sum(counts['vulnerabilities'].values())}; secret findings: {sum(counts['secrets'].values())}.")
        if 'scanReportError' in record:
            print(record['scanReportError'])
