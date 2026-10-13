"""Refresh AWS products and regions from the public AWS Health dashboard catalog."""
import hashlib
import json
from pathlib import Path
import urllib.request
ROOT = Path(__file__).resolve().parents[1]
SOURCE = 'https://d3s31nlw3sm5l8.cloudfront.net/services.json'
rows = json.loads(urllib.request.urlopen(SOURCE, timeout=30).read())
if not isinstance(rows, list) or len(rows) < 100: raise ValueError('Unexpected AWS catalog')
products = {}; regions = {}
for row in rows:
    if not row.get('service_name') or not row.get('service'): raise ValueError('Incomplete AWS service')
    products.setdefault(row['service_name'], set())
    if row.get('short_name'): products[row['service_name']].add(row['short_name'])
    if row.get('region_id') and row.get('region_name'): regions[row['region_id']] = row['region_name']
def service(identifier, name, category, components=None):
    return dict(id=identifier,name=name,category=category,provider='aws',statusUrl='https://health.aws.amazon.com/health/status',apiUrl='https://health.aws.amazon.com/public/currentevents',components=components,family='AWS')
entries = [service('extra.AWS','AWS · All services and regions','AWS services')]
for name, abbreviations in sorted(products.items()):
    short = ', '.join(a for a in sorted(abbreviations) if a.casefold() not in name.casefold())
    entries.append(service('aws.product.'+hashlib.sha256(name.encode()).hexdigest()[:16], 'AWS · '+name+(f' ({short})' if short else ''), 'AWS services', ['product:'+name]))
for identifier, name in sorted(regions.items()):
    entries.append(service('aws.region.'+identifier, f'AWS · {name} ({identifier})', 'AWS regions', ['region:'+identifier]))
catalog_file = ROOT/'src/StatusLookout.Core/Catalog/services.json'
existing = json.loads(catalog_file.read_text(encoding='utf-8'))
# Preserve old services present in the pinned parity catalog, and keep existing preference IDs.
by_id = {s['id']:s for s in existing if s['provider'] == 'aws'}
for entry in entries: by_id[entry['id']] = entry
catalog = [s for s in existing if s['provider'] != 'aws'] + list(by_id.values())
catalog_file.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
coverage_file = ROOT/'src/StatusLookout.Core/Catalog/stts-coverage.json'
if coverage_file.exists():
    coverage = json.loads(coverage_file.read_text(encoding='utf-8')); coverage['catalogCount'] = len(catalog)
    coverage_file.write_text(json.dumps(coverage,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
logo_folder = ROOT/'src/StatusLookout.Windows/Assets/ServiceLogos'
index_file = logo_folder/'index.json'; index = json.loads(index_file.read_text(encoding='utf-8'))
for entry in entries: index[entry['id']] = 'awsservices.png'
index_file.write_text(json.dumps(index,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(f'Added AWS aggregate, {len(products)} products and {len(regions)} regions. Total catalog: {len(catalog)}')
