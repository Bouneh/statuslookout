"""Import every entry from a pinned MIT-licensed stts catalog, preserving local IDs.

This imports endpoint metadata and brand artwork, not the macOS application.
The coverage report records the correspondence rather than comparing raw counts.
"""
import argparse, hashlib, json, re, urllib.request, urllib.parse
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--ref', required=True)
args = parser.parse_args()
if not re.fullmatch('[0-9a-f]{40}', args.ref): parser.error('A full commit SHA is required')
def fetch(path):
    req = urllib.request.Request(path, headers={'User-Agent': 'StatusLookout-catalog-sync'})
    with urllib.request.urlopen(req, timeout=45) as response: return response.read(16_000_000)
base = 'https://raw.githubusercontent.com/inket/stts/' + args.ref + '/'
source = json.loads(fetch(base + 'Resources/services.json'))
tree = json.loads(fetch('https://api.github.com/repos/inket/stts/git/trees/' + args.ref + '?recursive=1'))
logos = {Path(x['path']).stem: Path(x['path']).name for x in tree['tree'] if x['path'].startswith('Resources/Favicons/') and x['path'].endswith('.png')}
catalog_path = ROOT/'src/StatusLookout.Core/Catalog/services.json'
catalog = json.loads(catalog_path.read_text(encoding='utf-8'))
logo_dir = ROOT/'src/StatusLookout.Windows/Assets/ServiceLogos'
mapping = json.loads((logo_dir/'index.json').read_text(encoding='utf-8'))
coverage = []
def clean(name): return re.sub('[^a-z0-9]', '', name.lower())
def family(group, entry):
    return {'googlecloudplatform':'Google','firebase':'Google','awsservices':'AWS','awsregions':'AWS','apple':'Apple','appledeveloper':'Apple','azure':'Microsoft Azure','azuredevops':'Azure DevOps','adobe':'Adobe','salesforce':'Salesforce','sendbird':'Sendbird','miro':'Miro'}.get(group, re.sub(r'\s*\(.*', '', entry['name']))
independent = {
 'Algolia':('algolia','https://status.algolia.com/2/status/service/all/period/current'),
 'AppleAll':('apple','https://www.apple.com/support/systemstatus/data/system_status_en_US.js'),
 'AppleDeveloperAll':('apple','https://www.apple.com/support/systemstatus/data/developer/system_status_en_US.js'),
 'AzureAll':('azure','https://azure.status.microsoft/en-us/status'),
 'AzureDevOpsAll':('azuredevops','https://status.dev.azure.com'),
 'Beanstalk':('beanstalk','https://status.beanstalkapp.com'),
 'Broadcom':('broadcom','https://status.broadcom.com/api/v1/status'),
 'Fastly':('fastly','https://www.fastlystatus.com'),
 'Firebase':('google','https://status.firebase.google.com/incidents.json'),
 'Fivetran':('fivetran','https://status.fivetran.com/api/v1/status'),
 'GoogleCloudPlatformAll':('google','https://status.cloud.google.com/incidents.json'),
 'JSDelivr':('uptime','https://status.jsdelivr.com'), 'MiroAll':('incidentio','https://status.miro.com'),
 'Okta':('okta','https://status.okta.com'),
 'OracleNetSuite':('statuspage','https://status.netsuite.com/api/v2/status.json'),
 'Railway':('railway','https://status.railway.com/api/status'),
 'SendbirdAll':('aggregate','https://sendbird.com/status'),
 'Slack':('slack','https://slack-status.com/api/v2.0.0/current'),
 'Stripe':('stripe','https://status.stripe.com/current/full'),
 'Upsun':('upsun','https://status.upsun.com/data/incidents.json'),
 'UptimeDotCom':('uptime','https://status.uptime.com'),
 'Zendesk':('zendesk','https://status.zendesk.com/api/ssp/incidents.json')}
for group, entries in source.items():
    if group.startswith('_'): continue
    for entry in entries:
        name = entry['name']; fam = family(group, entry); components = None
        identifier = group + '.' + entry.get('class_name', re.sub('[^a-zA-Z0-9]', '', name))
        url = entry.get('url', ''); provider = group; api = url.rstrip('/')
        if group == 'independent':
            key = entry['class_name']
            if key.startswith('PayPal'):
                provider, api = 'paypal','https://www.paypal-status.com/api/v1/components'
                components = [('api' if 'API' in key else 'product')+':'+('sandbox' if 'Sandbox' in key else 'production')]; fam='PayPal'
            elif key.startswith('PSN'):
                region = {'PSNAmerica':'SCEA','PSNAsia':'SCEJA','PSNEurope':'SCEE'}[key]
                provider, api = 'psn',f'https://status.playstation.com/data/statuses/region/{region}.json'; fam='PlayStation Network'
            else: provider,api = independent[key]
            fam = {'AppleAll':'Apple','AppleDeveloperAll':'Apple','AzureAll':'Microsoft Azure','AzureDevOpsAll':'Azure DevOps','Firebase':'Google','GoogleCloudPlatformAll':'Google','SendbirdAll':'Sendbird','MiroAll':'Miro'}.get(key,fam)
        elif group in ('statuspage','sendbird'):
            provider='statuspage'; api=f"https://{entry['id']}.statuspage.io/api/v2/summary.json"
            if entry.get('host'):api=url.rstrip('/')+'/api/v2/summary.json'
        elif group == 'instatus': api += '/summary.json'
        elif group == 'statusiov1': provider='statusio'; api=f"https://status.io/1.0/status/{entry['id']}"
        elif group == 'cstate': api += '/index.json'
        elif group == 'lamb': api += '/api/components'
        elif group == 'site24x7': api += '/sp/api/public/summary_details/statuspages/'+urllib.parse.quote(entry['id'],safe='=')
        elif group == 'sorry': api='https://api.sorryapp.com/v1/pages/'+entry['id']+'/components'
        elif group == 'statuscake': api='https://app.statuscake.com/Workfloor/PublicReportHandler.php?PublicID='+entry['id']
        elif group == 'statushub': api += '/api/statuses'
        elif group == 'miro': provider='incidentio'
        elif group == 'incidentio' and name=='OpenAI':provider='statuspage';api='https://status.openai.com/api/v2/summary.json'
        elif group in ('apple','appledeveloper'):
            provider='apple'; components=[entry['id']]
            url='https://developer.apple.com/system-status/' if group=='appledeveloper' else 'https://www.apple.com/support/systemstatus/'
            api='https://www.apple.com/support/systemstatus/data/'+('developer/' if group=='appledeveloper' else '')+'system_status_en_US.js'
        elif group == 'azure': url=api='https://azure.status.microsoft/en-us/status'; components=[entry['id']]
        elif group == 'azuredevops': url=api='https://status.dev.azure.com'; components=[entry['id']]
        elif group == 'adobe':
            url='https://status.adobe.com'; api='https://data.status.adobe.com/adobestatus/StatusEvents'; components=[entry['id']]
        elif group == 'salesforce':
            url='https://status.salesforce.com'; api='https://api.status.salesforce.com/v1/instances/status/preview?products='+entry['product']
            components=None if entry.get('category') else [entry['id']]
        elif group in ('googlecloudplatform','firebase'):
            provider='google'; url='https://status.cloud.google.com' if group=='googlecloudplatform' else 'https://status.firebase.google.com'; api=url+'/incidents.json'; components=[entry['id']]
        elif group in ('awsservices','awsregions'):
            provider='aws';url='https://health.aws.amazon.com/health/status';api='https://health.aws.amazon.com/public/currentevents'
            components=['region:'+entry['id']] if group=='awsregions' else ['service:'+i for i in entry.get('ids',[entry['id']])]
            if entry['id']=='_all':components=None
            # Existing AWS product IDs stay stable; matching uses the full regional IDs from the source.
            match=next((s for s in catalog if s['provider']=='aws' and (s.get('components')==components or clean(s['name'].split(' (')[0].removeprefix('AWS · '))==clean(name))),None)
            if match and group=='awsservices' and not any(c.startswith('product:') for c in (match.get('components') or [])): match['components']=components
        else: match=None
        if group not in ('awsservices','awsregions'):
            match=next((s for s in catalog if s['provider']==provider and s['apiUrl'].rstrip('/')==api.rstrip('/') and s.get('components')==components),None)
        # Match names and aliases for endpoint migrations, but not a scoped entry against the whole page.
        if not match:
            names={clean(name)}|{clean(x.split('.')[-1]) for x in entry.get('old_names',[])}
            match=next((s for s in catalog if clean(s['name']) in names and bool(s.get('components'))==bool(components)),None)
        if not match and group in ('googlecloudplatform','firebase'):
            title=clean(re.sub(r'^(Google |Firebase )','',name))
            match=next((s for s in catalog if s['provider']=='google' and s['apiUrl']==api and clean(re.sub(r'^Google ','',s['name'].split(' · ')[-1]))==title),None)
        if match:
            identifier=match['id']; match.update(provider=provider,apiUrl=api,statusUrl=url,family=fam)
        else:
            if any(s['id']==identifier for s in catalog): identifier += '.'+hashlib.sha256((api+str(components)).encode()).hexdigest()[:10]
            match=dict(id=identifier,name=name,category=fam+' services' if group in ('apple','appledeveloper','azure','azuredevops','adobe','salesforce') else 'Developer services',provider=provider,statusUrl=url,apiUrl=api,components=components,family=fam)
            catalog.append(match)
        logo_key=group+'.'+re.sub('[^a-zA-Z0-9]', '',name)
        logo_key={'googlecloudplatform':'googlecloudplatform','firebase':'firebase','awsservices':'awsservices','awsregions':'awsregions','apple':'apple','appledeveloper':'appledeveloper','azure':'azure','azuredevops':'azuredevops','adobe':'adobe','salesforce':'salesforce','sendbird':'sendbird'}.get(group,logo_key)
        if group=='independent': logo_key='independent.'+entry['class_name']
        if group=='independent': logo_key={'AzureAll':'azure','AzureDevOpsAll':'azuredevops','AppleAll':'apple','AppleDeveloperAll':'appledeveloper','Firebase':'firebase','GoogleCloudPlatformAll':'googlecloudplatform','SendbirdAll':'sendbird'}.get(entry['class_name'],logo_key)
        if logo_key not in logos:
            alternatives=[group+'.'+x for x in entry.get('old_names',[])]+[x for x in entry.get('old_names',[])]
            logo_key=next((x for x in alternatives if x in logos),logo_key)
        if logo_key not in logos:
            candidates={clean(logo_key),clean(group+'.'+name),clean(group+'.'+name.replace(' (All Regions)','')),clean(group+'.'+entry.get('class_name','').replace('Production',''))}
            logo_key=next((key for key in logos if clean(key) in candidates),logo_key)
        if logo_key in logos: mapping[identifier]=logos[logo_key]
        elif identifier not in mapping: raise ValueError('Missing logo '+identifier+' / '+logo_key)
        coverage.append(dict(sourceGroup=group,sourceName=name,serviceId=identifier))
for service in catalog:
    if not service.get('family'):
        service['family']='Google' if service['provider']=='google' else 'AWS' if service['provider']=='aws' else re.sub(r'\s*\(.*','',service['name'])
catalog_path.write_text(json.dumps(catalog,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
(logo_dir/'index.json').write_text(json.dumps(mapping,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
report={'repository':'https://github.com/inket/stts','commit':args.ref,'sourceCount':len(coverage),'catalogCount':len(catalog),'entries':coverage}
(ROOT/'src/StatusLookout.Core/Catalog/stts-coverage.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
def download(filename):
    dest=logo_dir/filename
    if dest.exists(): return
    data=fetch(base+'Resources/Favicons/'+urllib.parse.quote(filename))
    if not data.startswith(b'\x89PNG\r\n\x1a\n'): raise ValueError('Invalid PNG '+filename)
    dest.write_bytes(data)
with ThreadPoolExecutor(max_workers=6) as pool: list(pool.map(download, sorted(set(mapping.values()))))
(logo_dir/'SOURCE.json').write_text(json.dumps({'repository':report['repository'],'commit':args.ref,'path':'Resources/Favicons','note':'Brand artwork belongs to its respective owners.'},indent=2)+'\n',encoding='utf-8')
print(f'{len(coverage)}/{len(coverage)} stts entries mapped; {len(catalog)} services; {len(set(mapping.values()))} logos.')
