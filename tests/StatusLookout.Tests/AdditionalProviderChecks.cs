using System.Text.Json;
using System.IO.Compression;
using System.Text;
using StatusLookout.Core;

internal static class AdditionalProviderChecks
{
    public static void Run(Action<string,Action> check)
    {
        ServiceSnapshot Parse(string provider,string body,string[]? scope=null)=>StatusParser.Parse(new("fixture","Fixture","Test",provider,"https://example.com","https://example.com",scope),body);
        void Is(ServiceStatus expected,ServiceSnapshot actual){if(expected!=actual.Status)throw new Exception($"Expected {expected}, got {actual.Status}");}
        void Reject(Action action){try{action();}catch(Exception e)when(e is InvalidDataException or JsonException){return;}throw new Exception("Invalid status was accepted.");}
        var samples=new Dictionary<string,string>
        {
            ["apple"]="""jsonCallback({"services":[{"serviceName":"iCloud","events":[]}]});""",
            ["adobe"]="""{"maintenanceEvent":{"maintenance":{}},"incidentEvent":{"incidents":{}}}""",
            ["salesforce"]="""[{"location":"EMEA","status":"OK"}]""",
            ["algolia"]="""{"global":{"status":"operational"}}""",
            ["broadcom"]="""{"page":{"state":"operational"}}""",
            ["fivetran"]="""{"groups":[{"status":"operational"}],"ungrouped_services":[]}""",
            ["paypal"]="""{"result":[{"category":{"name":"api"},"status":{"production":"Operational"}}]}""",
            ["psn"]="""{"regionName":"Europe","status":[]}""",
            ["railway"]="""{"activeIncidents":[]}""",
            ["stripe"]="""{"UptimeStatus":"up"}""",
            ["upsun"]="""{"incidents":[]}""",
            ["zendesk"]="""{"data":[]}""",
            ["cstate"]="""{"summaryStatus":"ok","systems":[]}""",
            ["lamb"]="""[{"name":"API","status":"Operational"}]""",
            ["site24x7"]="""{"data":{"current_status":[{"componentgroup_components":[{"component_status":1}]}]}}""",
            ["sorry"]="""{"response":[{"state":"operational"}]}""",
            ["statuscake"]="""{"TestData":[{"Status":"Up"}]}""",
            ["statushub"]="""{"counters":{"count_status_1":10,"count_status_2":0,"count_status_3":0}}""",
            ["azure"]="""<table class="status-table region-status-table" data-zone-name="europe"><tr><td><svg><use xlink:href="#svg-check"/></svg></td><td><svg><use xlink:href="#svg-health-unknown"/></svg></td></tr></table>""",
            ["azuredevops"]="""<script id="dataProviders">{"data":{"ms.vss-status-web.public-status-data-provider":{"serviceStatus":{"services":[{"id":"Boards","geographies":[{"name":"Europe","health":4}]}]}}}}</script>""",
            ["incidentio"]="""<main data-testid="components">All systems operational</main>""",
            ["betterstack"]="""<section><svg class="text-statuspage-green"></svg><h1 class="heading-small">All systems operational</h1></section>""",
            ["betteruptime"]="""<h1><svg class="text-statuspage-green"></svg>All systems operational</h1>""",
            ["pagerduty"]="""<script id="data">{"summary":{"open_incidents":[]}}</script>""",
            ["statuscast"]="""<span class="status-list-component-status-text component-available">Available</span>""",
            ["statuspal"]="""<div class="system-status type-none">Operational</div>""",
            ["beanstalk"]="""<div id="updates"><article><span class="status">ok</span></article></div>""",
            ["fastly"]="""<table><tr data-path="1"><td>API</td><td>Previous</td><td><i class="component-available"></i></td></tr></table>""",
            ["okta"]="""<span data-id="incidents">[]</span>""",
            ["uptime"]="""<script>window.status={"global_is_operational":true};</script>"""
        };
        foreach(var (provider,body) in samples)
        {
            check(provider+" public format is readable",()=>Is(ServiceStatus.Operational,Parse(provider,body)));
            check(provider+" rejects missing status markers",()=>Reject(()=>Parse(provider,"<html><h1>Access denied</h1></html>")));
        }
        check("Apple ignores resolved outages and scopes products",()=>{
            const string body="""{"services":[{"serviceName":"iCloud","events":[{"eventStatus":"resolved","statusType":"Outage"}]},{"serviceName":"Music","events":[{"eventStatus":"ongoing","statusType":"Outage"}]}]}""";
            Is(ServiceStatus.Operational,Parse("apple",body,["iCloud"]));Is(ServiceStatus.Outage,Parse("apple",body));Reject(()=>Parse("apple",body,["Missing"]));
        });
        check("Azure rejects absent regions and does not invent health from unavailable cells",()=>{
            Reject(()=>Parse("azure",samples["azure"],["china"]));Reject(()=>Parse("azure",samples["azure"].Replace("#svg-check","#svg-health-unknown")));
        });
        check("Salesforce separates regions and validates severity",()=>{
            const string body="""[{"location":"EMEA","status":"OK"},{"location":"NA","status":"MAJOR_INCIDENT_CORE"}]""";
            Is(ServiceStatus.Outage,Parse("salesforce",body));Is(ServiceStatus.Operational,Parse("salesforce",body,["EMEA"]));Reject(()=>Parse("salesforce",body,["Missing"]));
        });
        check("Incident.io current degraded icon is recognized",()=>Is(ServiceStatus.Degraded,Parse("incidentio","""<div data-testid="heads-up"><li><svg class="text-icon-degraded"></svg>We are experiencing issues</li></div>""")));
        check("PayPal unavailable environment values are skipped, empty results rejected",()=>{
            const string body="""{"result":[{"category":{"name":"api"},"status":{"production":""}},{"category":{"name":"api"},"status":{"production":"Service Outage"}}]}""";
            Is(ServiceStatus.Outage,Parse("paypal",body));Reject(()=>Parse("paypal","""{"result":[]}"""));
        });
        check("Gzip status bodies are decoded before parsing",()=>{
            using var stream=new MemoryStream();using(var zip=new GZipStream(stream,CompressionMode.Compress,true))zip.Write(Encoding.UTF8.GetBytes(samples["uptime"]));
            Is(ServiceStatus.Operational,Parse("uptime",StatusPayload.Decode(stream.ToArray())));
        });
        check("All entries in the pinned stts catalog have valid local counterparts",()=>{
            var assembly=typeof(AdditionalProviderChecks).Assembly;using var source=assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(n=>n.EndsWith("stts-coverage.json")))!;
            using var json=JsonDocument.Parse(source);var catalog=ServiceCatalog.Load().ToDictionary(s=>s.Id);var entries=json.RootElement.GetProperty("entries").EnumerateArray().ToArray();
            if(entries.Length!=json.RootElement.GetProperty("sourceCount").GetInt32()||entries.Length<1000||catalog.Count<entries.Length)throw new Exception("Incomplete catalog coverage.");
            foreach(var entry in entries) {var service=catalog[entry.GetProperty("serviceId").GetString()!];service.Validate();if(string.IsNullOrWhiteSpace(service.Family))throw new Exception("Family missing.");}
        });
    }
}
