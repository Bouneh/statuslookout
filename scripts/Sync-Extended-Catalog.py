"""Add verified public status sources and official favicons (Python 3 + Pillow).

Existing IDs, watchlists and component preferences are preserved.
"""
import io
import json
import urllib.request
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[1]
CATALOG = ROOT / 'src/StatusLookout.Core/Catalog/services.json'
LOGOS = ROOT / 'src/StatusLookout.Windows/Assets/ServiceLogos'

def fetch(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'StatusLookout-catalog-sync (+https://github.com/Bouneh/statuslookout)'})
    with urllib.request.urlopen(request, timeout=30) as response:
        return response.read(16_000_000)

definitions = []
def add(key, name, family, provider, url, api=None):
    definitions.append(dict(id='extended.' + key, name=name, category='Developer services',
                            provider=provider, statusUrl=url, apiUrl=api or url, family=family))

for key, title, host in [
    ('PublicCloud', 'Public Cloud', 'public-cloud'),
    ('BareMetal', 'Bare Metal Cloud', 'bare-metal-servers'),
    ('PrivateCloud', 'Hosted Private Cloud', 'hosted-private-cloud'),
    ('WebCloud', 'Web Cloud', 'web-cloud'),
    ('Network', 'Network & Infrastructure', 'network'),
    ('Support', 'Customer Service', 'customer-service'),
]:
    url = 'https://' + host + '.status-ovhcloud.com/'
    add('OVHcloud.' + key, 'OVHcloud · ' + title, 'OVHcloud', 'statuspage', url, url + 'api/v2/summary.json')

for key, name, provider, url, api in [
    ('Hetzner', 'Hetzner', 'hetzner', 'https://status.hetzner.com/', None),
    ('Supabase', 'Supabase', 'statuspage', 'https://status.supabase.com/', 'api/v2/summary.json'),
    ('Render', 'Render', 'statuspage', 'https://status.render.com/', 'api/v2/summary.json'),
    ('Mistral', 'Mistral AI', 'statuspage', 'https://status.mistral.ai/', 'api/v2/summary.json'),
    ('Groq', 'Groq', 'statuspage', 'https://groqstatus.com/', 'api/v2/summary.json'),
    ('HuggingFace', 'Hugging Face', 'betterstack-json', 'https://status.huggingface.co/', 'index.json'),
    ('Resend', 'Resend', 'statuspage', 'https://resend-status.com/', 'api/v2/summary.json'),
    ('Tailscale', 'Tailscale', 'statuspage', 'https://status.tailscale.com/', 'api/v2/summary.json'),
    ('Streamlabs', 'Streamlabs', 'streamlabs', 'https://status.logitech.com/', 'api/v2/summary.json'),
]:
    add(key, name, name, provider, url, url + api if api else url)

sources = {
    'OVHcloud': 'https://www.ovhcloud.com/apple-touch-icon.png',
    'Hetzner': 'https://www.hetzner.com/_resources/themes/hetzner/images/favicons/apple-icon-120x120.png',
    'Supabase': 'https://supabase.com/favicon/apple-icon-180x180.png',
    'Render': 'https://dka575ofm4ao0.cloudfront.net/pages-favicon_logos/original/89884/favicon-dark-633ba9a9-4b6f-413d-a316-b1dc3ed9c7bb.png',
    'Mistral AI': 'https://mistral.ai/favicon.ico',
    'Groq': 'https://groq.com/apple-touch-icon.png',
    'Hugging Face': 'https://huggingface.co/favicon.ico',
    'Resend': 'https://resend.com/static/favicons/favicon-marketing@120x120.png?v=1',
    'Tailscale': 'https://tailscale.com/favicon.png',
    'Streamlabs': 'https://cdn.streamlabs.com/static/imgs/streamlabs-logos/favicon/apple-touch-icon.png',
}

if __name__ == '__main__':
    # Fetch all sources before changing tracked catalog data.
    for url in dict.fromkeys(s['apiUrl'] for s in definitions): fetch(url)
    artwork = {}
    for family, url in sources.items():
        image = Image.open(io.BytesIO(fetch(url))).convert('RGBA')
        image.thumbnail((180, 180))
        output = io.BytesIO(); image.save(output, format='PNG')
        artwork[family] = output.getvalue()
    catalog = json.loads(CATALOG.read_text(encoding='utf-8'))
    by_id = {s['id']: s for s in catalog}
    by_id.update({s['id']: s for s in definitions})
    index_path = LOGOS / 'index.json'
    index = json.loads(index_path.read_text(encoding='utf-8'))
    for family, data in artwork.items():
        filename = 'extended-' + ''.join(c.lower() for c in family if c.isalnum()) + '.png'
        (LOGOS / filename).write_bytes(data)
        for service in definitions:
            if service['family'] == family: index[service['id']] = filename
    CATALOG.write_text(json.dumps(list(by_id.values()), indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    index_path.write_text(json.dumps(index, indent=2, ensure_ascii=False) + '\n', encoding='utf-8')
    print(f'Added/refreshed {len(definitions)} entries across {len(sources)} providers; catalog: {len(by_id)}.')
