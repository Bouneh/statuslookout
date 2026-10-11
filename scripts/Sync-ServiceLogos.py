"""Refresh bundled brand favicons from a pinned stts commit (Python 3, stdlib only)."""
from pathlib import Path
from concurrent.futures import ThreadPoolExecutor
import argparse, json, urllib.request, urllib.parse

ROOT = Path(__file__).resolve().parents[1]
DEST = ROOT / 'src/StatusLookout.Windows/Assets/ServiceLogos'
parser = argparse.ArgumentParser()
parser.add_argument('--ref', required=True, help='Full stts Git commit SHA')
args = parser.parse_args()
if len(args.ref) != 40 or any(c not in '0123456789abcdef' for c in args.ref):
    parser.error('Use a full lowercase commit SHA.')

def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'StatusLookout-logo-sync'})
    with urllib.request.urlopen(request, timeout=30) as response:
        data = response.read(2_000_001)
    if len(data) > 2_000_000: raise ValueError('Logo source is too large')
    return data

tree = json.loads(fetch('https://api.github.com/repos/inket/stts/git/trees/' + args.ref + '?recursive=1'))
paths = {Path(x['path']).stem: x['path'] for x in tree['tree'] if x['path'].startswith('Resources/Favicons/') and x['path'].endswith('.png')}
services = json.loads((ROOT / 'src/StatusLookout.Core/Catalog/services.json').read_text(encoding='utf-8'))
mapping = {}
for service in services:
    key = service['id']
    if key == 'extra.OpenAI': key = 'incidentio.OpenAI'
    elif key == 'extra.Slack': key = 'independent.Slack'
    elif service['name'].startswith('Google Cloud'): key = 'googlecloudplatform'
    elif service['name'].startswith('Firebase'): key = 'firebase'
    elif key.startswith('sendbird.'): key = 'sendbird'
    if key not in paths: raise ValueError('No source logo for ' + service['id'])
    mapping[service['id']] = Path(paths[key]).name

DEST.mkdir(parents=True, exist_ok=True)
def download(filename):
    url = 'https://raw.githubusercontent.com/inket/stts/' + args.ref + '/Resources/Favicons/' + urllib.parse.quote(filename)
    data = fetch(url)
    if not data.startswith(b'\x89PNG\r\n\x1a\n'): raise ValueError('Invalid PNG: ' + filename)
    (DEST / filename).write_bytes(data)
with ThreadPoolExecutor(max_workers=6) as pool:
    list(pool.map(download, sorted(set(mapping.values()))))
(DEST / 'index.json').write_text(json.dumps(mapping, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
(DEST / 'SOURCE.json').write_text(json.dumps({'repository': 'https://github.com/inket/stts', 'commit': args.ref,
    'path': 'Resources/Favicons', 'note': 'Brand artwork belongs to its respective owners; used solely to identify monitored services. Google Cloud and Firebase products share their platform logo.'}, indent=2) + '\n', encoding='utf-8')
print(f'Bundled {len(set(mapping.values()))} logos covering {len(mapping)} services.')
