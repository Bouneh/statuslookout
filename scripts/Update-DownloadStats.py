"""Collect cumulative GitHub release counters and render honest monthly README charts."""
from __future__ import annotations
import argparse
import calendar
from datetime import datetime, timezone
import html
import json
import math
import os
from pathlib import Path
import re
import urllib.request

ROOT = Path(__file__).resolve().parents[1]
RUNTIMES = ('x64', 'x86', 'arm64')
COLORS = {'x64': '#82AEFF', 'x86': '#51DCC1', 'arm64': '#B6A1FF'}
EXECUTABLE = re.compile(r'^(?:StatusLookout|UptimeKite)-(?:Setup-)?win-(x64|x86|arm64)\.exe$')

def fetch_releases(repository: str) -> list[dict]:
    if not re.fullmatch(r'[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+', repository):
        raise ValueError('Invalid GitHub repository slug')
    releases = []
    headers = {'User-Agent': 'StatusLookout-download-stats', 'Accept': 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28'}
    if os.environ.get('GITHUB_TOKEN'): headers['Authorization'] = 'Bearer ' + os.environ['GITHUB_TOKEN']
    for page in range(1, 1001):
        url = f'https://api.github.com/repos/{repository}/releases?per_page=100&page={page}'
        with urllib.request.urlopen(urllib.request.Request(url, headers=headers), timeout=30) as response:
            items = json.load(response)
        if not isinstance(items, list): raise ValueError('Unexpected GitHub response')
        releases.extend(items)
        if len(items) < 100: return releases
    raise ValueError('Release pagination exceeded the collection limit')

def observe(previous: dict | None, releases: list[dict], now: datetime, repository: str) -> dict:
    stamp = now.astimezone(timezone.utc).isoformat(timespec='seconds').replace('+00:00', 'Z')
    month = stamp[:7]
    if previous is not None and (previous.get('schema') != 1 or previous.get('repository') != repository):
        raise ValueError('The statistics history belongs to a different repository or schema')
    state = json.loads(json.dumps(previous)) if previous else {
        'schema': 1, 'repository': repository, 'tracking_started_at': stamp,
        'assets': {}, 'months': {}, 'before_tracking': 0,
    }
    bucket = state['months'].setdefault(month, dict.fromkeys(RUNTIMES, 0) | {'samples': 0})
    bucket['samples'] += 1
    current = {}; stable_releases = set()
    for release in releases:
        if release.get('draft') or release.get('prerelease'): continue
        for asset in release.get('assets', []):
            match = EXECUTABLE.fullmatch(asset.get('name', ''))
            if not match: continue
            identifier = str(asset['id'])
            if identifier in current: continue  # Pagination can overlap while a new release is published.
            count = asset['download_count']
            if not isinstance(count, int) or count < 0: raise ValueError('Invalid download counter')
            created = datetime.fromisoformat(asset['created_at'].replace('Z', '+00:00')).astimezone(timezone.utc)
            if created > now: raise ValueError('Asset creation date is in the future')
            runtime = match[1]; old = state['assets'].get(identifier)
            if old:
                delta = max(0, count - old['count'])
            elif created.strftime('%Y-%m') == month:
                # A new asset created this month cannot have downloads from earlier months.
                delta = count
            else:
                # GitHub exposes no event dates: older unseen counters are an unallocated baseline.
                delta = 0; state['before_tracking'] += count
            bucket[runtime] += delta
            current[identifier] = {'count': count, 'runtime': runtime, 'name': asset['name'], 'last_seen': stamp}
            state['assets'][identifier] = current[identifier] | {'count': max(count, old['count']) if old else count}
            stable_releases.add(release['tag_name'])
    state['last_observed_at'] = stamp
    state['github_total'] = sum(asset['count'] for asset in current.values())
    state['stable_release_count'] = len(stable_releases)
    return state

def months_between(first: str, last: str) -> list[str]:
    year, month = map(int, first.split('-')); values = []
    while f'{year:04}-{month:02}' <= last:
        values.append(f'{year:04}-{month:02}')
        year, month = (year + 1, 1) if month == 12 else (year, month + 1)
    return values[-12:]

def render_chart(state: dict) -> str:
    month = state['last_observed_at'][:7]
    periods = months_between(state['tracking_started_at'][:7], month)
    series = [state['months'].get(period) for period in periods]
    totals = [sum(row.get(runtime, 0) for runtime in RUNTIMES) if row else None for row in series]
    highest = max([value for value in totals if value is not None] + [1])
    raw_step = max(1, highest / 4)
    unit = 10 ** math.floor(math.log10(raw_step))
    step = next(value * unit for value in (1, 2, 5, 10) if value * unit >= raw_step)
    ceiling = max(step, math.ceil(highest * 1.1 / step) * step)
    first = state['tracking_started_at'][:10]
    updated = state['last_observed_at'].replace('T', ' ').replace('Z', ' UTC')
    this_month = sum(state['months'][month][runtime] for runtime in RUNTIMES)
    parts = [f'''<svg xmlns="http://www.w3.org/2000/svg" width="1000" height="430" viewBox="0 0 1000 430" role="img" aria-labelledby="title desc">
<title id="title">StatusLookout monthly release downloads</title>
<desc id="desc">Daily snapshots of GitHub stable Windows executable download counters. History starts {first}. Downloads are not unique users. Missing months have no samples. Last updated {html.escape(updated)}.</desc>
<defs><linearGradient id="card" x2="1" y2="1"><stop stop-color="#141F33"/><stop offset="1" stop-color="#182840"/></linearGradient></defs>
<rect x="1" y="1" width="998" height="428" rx="18" fill="#0C1220" stroke="#25334B"/>
<g font-family="Segoe UI,Arial,sans-serif">
<circle cx="36" cy="34" r="5" fill="#51DCC1"/>
<text x="52" y="39" fill="#F1F5FF" font-size="17" font-weight="700">StatusLookout · Monthly downloads</text>
<text x="28" y="61" fill="#94A4BF" font-size="12">Stable Windows executables · GitHub Releases · No application telemetry</text>
<text x="972" y="37" text-anchor="end" fill="#94A4BF" font-size="10">{html.escape(updated)}</text>''']
    for x, label, value, color in [(28, 'GITHUB TOTAL', state['github_total'], '#82AEFF'), (351, 'COUNTED THIS MONTH', this_month, '#51DCC1'), (674, 'STABLE RELEASES', state['stable_release_count'], '#B6A1FF')]:
        parts.append(f'<rect x="{x}" y="82" width="298" height="76" rx="11" fill="url(#card)" stroke="#25334B"/><text x="{x+18}" y="105" fill="#94A4BF" font-size="10" font-weight="600">{label}</text><text x="{x+18}" y="140" fill="{color}" font-size="29" font-weight="700">{value:,}</text>')
    left, top, width, height = 65, 194, 887, 142
    for tick in range(0, int(ceiling) + 1, int(step)):
        y = top + height - height * tick / ceiling
        parts.append(f'<path d="M{left} {y:.1f}h{width}" stroke="#25334B" stroke-dasharray="3 6"/><text x="{left-12}" y="{y+4:.1f}" text-anchor="end" fill="#71839F" font-size="10">{tick:,}</text>')
    column = width / len(periods); bar_width = min(62, column * .58)
    for index, (period, row, total) in enumerate(zip(periods, series, totals)):
        center = left + column * (index + .5); base = top + height
        label = calendar.month_abbr[int(period[5:])] + ' ' + period[2:4]
        if row:
            parts.append(f'<g><title>{period}: {total} counted downloads · x64 {row["x64"]}, x86 {row["x86"]}, ARM64 {row["arm64"]}</title>')
            for runtime in RUNTIMES:
                segment = height * row[runtime] / ceiling
                if segment:
                    base -= segment
                    parts.append(f'<rect x="{center-bar_width/2:.1f}" y="{base:.1f}" width="{bar_width:.1f}" height="{segment:.1f}" rx="2" fill="{COLORS[runtime]}"/>')
            parts.append(f'<text x="{center:.1f}" y="{base-9:.1f}" text-anchor="middle" fill="#F1F5FF" font-size="12" font-weight="700">{total:,}</text></g>')
        else:
            parts.append(f'<text x="{center:.1f}" y="{base-12:.1f}" text-anchor="middle" fill="#71839F" font-size="14">—<title>No observations recorded for {period}</title></text>')
        parts.append(f'<text x="{center:.1f}" y="361" text-anchor="middle" fill="#A5B3CD" font-size="11">{label}</text>')
    for x, runtime, label in [(377, 'x64', 'x64'), (461, 'x86', 'x86'), (545, 'arm64', 'ARM64')]:
        parts.append(f'<rect x="{x}" y="383" width="9" height="9" rx="2" fill="{COLORS[runtime]}"/><text x="{x+16}" y="392" fill="#A5B3CD" font-size="11">{label}</text>')
    parts.append(f'<text x="28" y="414" fill="#71839F" font-size="10">Daily counter changes · Month of observation (UTC) · Tracking since {first} · Not unique users</text></g></svg>\n')
    return '\n'.join(parts)

def render_badge(total: int) -> str:
    text = f'{total:,}'; right_width = max(36, len(text) * 8 + 18); width = 114 + right_width
    return f'''<svg xmlns="http://www.w3.org/2000/svg" width="{width}" height="20" role="img" aria-label="Executable downloads: {total}">
<title>GitHub release executable downloads: {total}</title><clipPath id="clip"><rect width="{width}" height="20" rx="3"/></clipPath>
<g clip-path="url(#clip)"><path fill="#263952" d="M0 0h114v20H0z"/><path fill="#456FE3" d="M114 0h{right_width}v20H114z"/></g>
<g fill="#fff" text-anchor="middle" font-family="Verdana,Arial,sans-serif" font-size="11"><text x="57" y="14">downloads .exe</text><text x="{114+right_width/2}" y="14">{text}</text></g></svg>\n'''

def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repository', default=os.environ.get('GITHUB_REPOSITORY', 'Bouneh/statuslookout'))
    parser.add_argument('--releases-file', type=Path, help='Use an API response fixture instead of accessing GitHub')
    args = parser.parse_args()
    state_file = ROOT / 'docs/metrics/downloads.json'
    previous = json.loads(state_file.read_text(encoding='utf-8')) if state_file.exists() else None
    releases = json.loads(args.releases_file.read_text(encoding='utf-8-sig')) if args.releases_file else fetch_releases(args.repository)
    state = observe(previous, releases, datetime.now(timezone.utc), args.repository)
    state_file.parent.mkdir(parents=True, exist_ok=True)
    state_file.write_text(json.dumps(state, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    (ROOT / 'assets/downloads-monthly.svg').write_text(render_chart(state), encoding='utf-8')
    (ROOT / 'assets/downloads-badge.svg').write_text(render_badge(state['github_total']), encoding='utf-8')
    print(f'Observed {state["github_total"]} executable downloads across {state["stable_release_count"]} stable releases.')

if __name__ == '__main__': main()
