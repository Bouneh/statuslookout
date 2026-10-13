using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
namespace StatusLookout.Core;

/// <summary>Adapters for the public status sources used by the imported catalog.
/// Missing markers and changed schemas fail closed as unknown, never as healthy.</summary>
public static class AdditionalStatusProviders
{
    public static readonly HashSet<string> Providers = ["apple", "adobe", "azure", "azuredevops", "salesforce", "algolia", "broadcom", "fivetran", "paypal", "psn", "railway", "stripe", "upsun", "zendesk", "cstate", "lamb", "site24x7", "sorry", "statuscake", "statushub", "incidentio", "betterstack", "betteruptime", "pagerduty", "statuscast", "statuspal", "beanstalk", "fastly", "okta", "uptime", "aggregate"];
    private static JsonElement Field(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) ? v : throw new InvalidDataException("Missing status field: " + key);
    private static string Text(JsonElement e, string key) => Field(e,key).ToString();
    private static string Optional(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key,out var v) ? v.ToString() : "";
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToArray() : throw new InvalidDataException("Expected a status array.");
    private static IEnumerable<JsonElement> Values(JsonElement e) => e.ValueKind == JsonValueKind.Object ? e.EnumerateObject().Select(p=>p.Value).ToArray() : throw new InvalidDataException("Expected a status object.");
    private static ServiceStatus Map(string value) => value.Trim().ToLowerInvariant().Replace(' ','_').Replace('-','_') switch
    {
        "operational" or "available" or "good" or "ok" or "up" or "all_good" or "none" or "informational" or "information" or "notice" or "monitored" or "identified" or "monitoring" => ServiceStatus.Operational,
        "maintenance" or "under_maintenance" or "undermaintenance" or "scheduled" or "pending" or "paused" => ServiceStatus.Maintenance,
        "minor" or "degraded" or "degraded_performance" or "partial_outage" or "partially_degraded" or "disrupted" or "service_disruption" or "performance" or "issue" or "investigating" or "potential" or "trivial" => ServiceStatus.Degraded,
        "major" or "critical" or "outage" or "major_outage" or "full_outage" or "unavailable" or "down" or "downtime" or "service_outage" or "problem" => ServiceStatus.Outage,
        _ => throw new InvalidDataException("Unrecognized status: " + value)
    };
    private static ServiceStatus Worst(IEnumerable<ServiceStatus> states, bool allowEmpty = false)
    {
        var values = states.ToArray();
        return values.Length > 0 ? values.Max() : allowEmpty ? ServiceStatus.Operational : throw new InvalidDataException("No status information was found.");
    }
    public static ServiceSnapshot Parse(ServiceDefinition service, string payload, DateTimeOffset now)
    {
        ServiceStatus status;
        string? identity = null;
        if (service.Provider is "azure" or "azuredevops" or "incidentio" or "betterstack" or "betteruptime" or "pagerduty" or "statuscast" or "statuspal" or "beanstalk" or "fastly" or "okta" or "uptime")
            status = Html(service, payload);
        else
        {
            if (service.Provider == "apple")
            {
                var start=payload.IndexOf('{'); var end=payload.LastIndexOf('}');
                if(start<0||end<start) throw new InvalidDataException("Missing Apple status data.");
                payload=payload[start..(end+1)];
            }
            using var document = JsonDocument.Parse(payload);
            var r=document.RootElement;
            var selected=service.Components??[];
            bool Select(string key) => selected.Length==0||selected.Contains(key,StringComparer.OrdinalIgnoreCase);
            switch (service.Provider)
            {
                case "apple":
                    var apple=Items(Field(r,"services")).Where(s=>Select(Text(s,"serviceName"))).ToArray();
                    if(selected.Any(id=>!apple.Any(s=>Text(s,"serviceName")==id))) throw new InvalidDataException("Apple service missing.");
                    status=Worst(apple.Select(s=>Worst(Items(Field(s,"events")).Where(e=>Text(e,"eventStatus")=="ongoing").Select(e=>Map(Text(e,"statusType"))),true)));
                    break;
                case "adobe":
                    var maintenance=Values(Field(Field(r,"maintenanceEvent"),"maintenance"));
                    var incidents=Values(Field(Field(r,"incidentEvent"),"incidents"));
                    var states=new List<ServiceStatus>(); var ids=new List<string>();
                    foreach(var m in maintenance.Where(e=>Text(e,"status").Equals("started",StringComparison.OrdinalIgnoreCase)))
                        if(Values(Field(m,"products")).Concat(Values(Field(m,"clouds"))).Any(p=>Select(Text(p,"id")))) { states.Add(ServiceStatus.Maintenance); ids.Add(Text(m,"id")); }
                    foreach(var incident in incidents)
                    {
                        var cloudMatch=Values(Field(incident,"clouds")).Any(c=>Select(Text(c,"id")));
                        foreach(var product in Values(Field(incident,"products")))
                        {
                            if(!cloudMatch&&!Select(Text(product,"id"))) continue;
                            var history=Field(product,"history").EnumerateObject().OrderBy(h=>h.Name,StringComparer.Ordinal).Select(h=>h.Value).ToArray();
                            if(history.Length==0) continue;
                            var latest=history[^1]; var phase=Text(latest,"status").ToLowerInvariant();
                            if(phase is "closed" or "canceled" or "completed" or "dismissed") continue;
                            if(phase is not ("opened" or "started" or "updated" or "reopened" or "discovery" or "scheduled")) throw new InvalidDataException("Unknown Adobe event phase.");
                            states.Add(Map(Text(latest,"severity")));ids.Add(Text(incident,"id"));
                        }
                    }
                    status=Worst(states,true);identity=ids.Count==0?null:JsonSerializer.Serialize(ids.Distinct().Order());break;
                case "salesforce":
                    var instances=Items(r).Where(i=>Select(Text(i,"location")));
                    status=Worst(instances.Select(i=>Text(i,"status") switch {
                        "OK"=>ServiceStatus.Operational,"MAINTENANCE_NONCORE" or "MAINTENANCE_CORE"=>ServiceStatus.Maintenance,
                        "MINOR_INCIDENT_NONCORE" or "MINOR_INCIDENT_CORE" or "MAJOR_INCIDENT_NONCORE"=>ServiceStatus.Degraded,
                        "MAJOR_INCIDENT_CORE"=>ServiceStatus.Outage,_=>throw new InvalidDataException("Unknown Salesforce status.") }));break;
                case "algolia": status=Map(Text(Field(r,"global"),"status"));break;
                case "broadcom": status=Map(Text(Field(r,"page"),"state"));break;
                case "fivetran": status=Worst(Items(Field(r,"groups")).Concat(Items(Field(r,"ungrouped_services"))).Select(i=>Map(Text(i,"status"))));break;
                case "paypal":
                    var scope=(selected.SingleOrDefault()??"api:production").Split(':');
                    status=Worst(Items(Field(r,"result")).Where(i=>Text(Field(i,"category"),"name")==scope[0]).Select(i=>Text(Field(i,"status"),scope[1])).Where(s=>s.Length>0).Select(Map));break;
                case "psn": status=Worst(Items(Field(r,"status")).Select(i=>Map(Text(i,"statusType"))),true);break;
                case "railway": status=Worst(Items(Field(r,"activeIncidents")).SelectMany(i=>Items(Field(i,"components"))).Select(i=>Map(Text(i,"impact"))),true);break;
                case "stripe": status=Map(Text(r,"UptimeStatus"));break;
                case "upsun": status=Worst(Items(Field(r,"incidents")).Where(i=>Optional(i,"closed_at").Length==0).SelectMany(i=>Items(Field(i,"components"))).Select(i=>Map(Text(i,"status"))),true);break;
                case "zendesk":
                    status=Worst(Items(Field(r,"data")).Select(i=>Field(i,"attributes")).Where(i=>Optional(i,"resolvedAt").Length==0).Select(i=>Field(i,"outage").GetBoolean()?ServiceStatus.Outage:ServiceStatus.Degraded),true);break;
                case "cstate": status=Map(Text(r,"summaryStatus"));break;
                case "lamb": status=Worst(Items(r).Select(i=>Map(Text(i,"status"))));break;
                case "site24x7":
                    IEnumerable<JsonElement> Flatten(JsonElement items) => Items(items).SelectMany(i=>i.TryGetProperty("componentgroup_components",out var children)?Flatten(children):[i]);
                    status=Worst(Flatten(Field(Field(r,"data"),"current_status")).Select(i=>Field(i,"component_status").GetInt32() switch {
                        1 or 2=>ServiceStatus.Operational,4=>ServiceStatus.Maintenance,3 or 5=>ServiceStatus.Degraded,6=>ServiceStatus.Outage,_=>throw new InvalidDataException("Unknown Site24x7 status.")}));break;
                case "sorry": status=Worst(Items(Field(r,"response")).Select(i=>Text(i,"state")=="degraded"?ServiceStatus.Outage:Map(Text(i,"state"))));break;
                case "statuscake": status=Worst(Items(Field(r,"TestData")).Select(i=>Map(Text(i,"Status"))));break;
                case "statushub":
                    var counters=Field(r,"counters");status=Field(counters,"count_status_3").GetInt32()>0?ServiceStatus.Outage:Field(counters,"count_status_2").GetInt32()>0?ServiceStatus.Degraded:Field(counters,"count_status_1").GetInt32()>0?ServiceStatus.Operational:throw new InvalidDataException("Empty StatusHub counters.");break;
                default: throw new InvalidDataException("Unsupported adapter.");
            }
        }
        return new(service.Id,status,StatusParser.DefaultMessage(status),now,status==ServiceStatus.Operational?null:identity);
    }
    private static ServiceStatus Html(ServiceDefinition service,string payload)
    {
        var doc=new HtmlDocument();doc.LoadHtml(payload);
        IEnumerable<HtmlNode> Nodes(string xpath) => doc.DocumentNode.SelectNodes(xpath)?.AsEnumerable()??[];
        HtmlNode Required(string xpath) => Nodes(xpath).FirstOrDefault()??throw new InvalidDataException("Missing status page marker.");
        string Classes(HtmlNode n)=>n.GetAttributeValue("class","");
        string Content(HtmlNode n)=>WebUtility.HtmlDecode(n.InnerText).Trim();
        string ClassXPath(string cls)=>"//*[contains(concat(' ',normalize-space(@class),' '),' "+cls+" ')]";
        switch(service.Provider)
        {
            case "azure":
                var tables=Nodes("//table[contains(@class,'region-status-table')]").Where(n=>service.Components is not {Length:>0}||service.Components.Contains(n.GetAttributeValue("data-zone-name",""))).ToArray();
                if(service.Components?.Any(id=>!tables.Any(t=>t.GetAttributeValue("data-zone-name","")==id))==true) throw new InvalidDataException("Azure region missing.");
                return Worst(tables.SelectMany(t=>t.Descendants("use")).Select(n=>n.GetAttributeValue("xlink:href","")).Where(s=>s.StartsWith("#svg-")&&s is not ("#svg-health-unknown" or "#svg-information-icon")).Select(s=>s switch{"#svg-check"=>ServiceStatus.Operational,"#svg-health-warning"=>ServiceStatus.Degraded,"#svg-health-error"=>ServiceStatus.Outage,"#svg-health-information"=>ServiceStatus.Operational,_=>throw new InvalidDataException("Unknown Azure marker.")}));
            case "azuredevops":
                using(var json=JsonDocument.Parse(Required("//script[@id='dataProviders']").InnerText))
                {
                    var services=Items(Field(Field(Field(Field(json.RootElement,"data"),"ms.vss-status-web.public-status-data-provider"),"serviceStatus"),"services")).Where(i=>service.Components is not {Length:>0}||service.Components.Contains(Text(i,"id"))).ToArray();
                    if(service.Components?.Any(id=>!services.Any(s=>Text(s,"id")==id))==true) throw new InvalidDataException("Azure DevOps service missing.");
                    return Worst(services.SelectMany(s=>Items(Field(s,"geographies"))).Select(g=>Field(g,"health").GetInt32() switch{1=>ServiceStatus.Outage,2=>ServiceStatus.Degraded,3 or 4=>ServiceStatus.Operational,_=>throw new InvalidDataException("Unknown Azure DevOps health.")}));
                }
            case "incidentio":
                var heads=Nodes("//*[@data-testid='heads-up']").FirstOrDefault();
                if(heads==null)
                {
                    // Require the actual page's component summary before accepting an absent incident banner.
                    if(!payload.Contains("All systems operational",StringComparison.OrdinalIgnoreCase)&&!payload.Contains("All Systems Operational",StringComparison.OrdinalIgnoreCase)&&!payload.Contains("data-testid=\"component",StringComparison.Ordinal)) throw new InvalidDataException("Unrecognized incident.io page.");
                    return ServiceStatus.Operational;
                }
                return Worst(heads.Descendants("svg").Select(n=>Regex.Match(Classes(n),"text-icon-(operational|degraded-performance|degraded|partial-outage|full-outage|under-maintenance)").Groups[1].Value).Where(s=>s.Length>0).Select(Map));
            case "betterstack":
                var heading=Nodes(ClassXPath("heading-large")).Concat(Nodes(ClassXPath("heading-small"))).FirstOrDefault()??throw new InvalidDataException("Missing Better Stack heading.");
                var icon=heading.ParentNode?.Descendants("svg").FirstOrDefault()??throw new InvalidDataException("Missing Better Stack icon.");
                var color=Regex.Match(Classes(icon),"text-statuspage-(green|red|blue|yellow)").Groups[1].Value;
                if(color.Length==0) color=icon.Descendants("path").Select(p=>p.GetAttributeValue("fill","").ToLowerInvariant()).FirstOrDefault()??"";
                return color switch{"green" or "#059669"=>ServiceStatus.Operational,"red" or "#b91c1c"=>ServiceStatus.Outage,"blue" or "#0369a1"=>ServiceStatus.Maintenance,"yellow" or "#d97706"=>ServiceStatus.Degraded,_=>throw new InvalidDataException("Unknown Better Stack icon.")};
            case "betteruptime":
                var overview=Nodes(ClassXPath("status-page__overview-icon")).FirstOrDefault()??Nodes("//h1//*[name()='svg']").FirstOrDefault()??throw new InvalidDataException("Missing Better Uptime overview.");
                var modern=Regex.Match(Classes(overview),"text-statuspage-(green|red|blue|yellow|neutral)");
                if(modern.Success) return modern.Groups[1].Value switch{"green"=>ServiceStatus.Operational,"red"=>ServiceStatus.Outage,"blue"=>ServiceStatus.Maintenance,"yellow"=>ServiceStatus.Degraded,_=>ServiceStatus.Unknown};
                var match=Regex.Match(Classes(overview),"(?:icon-|--)(operational|degraded|downtime|maintenance|not-monitored)");
                if(!match.Success) match=Regex.Match(Classes(overview),"(operational|degraded|downtime|maintenance)");
                if(!match.Success) throw new InvalidDataException("Unknown Better Uptime overview.");
                return match.Groups[1].Value=="not-monitored"?ServiceStatus.Unknown:Map(match.Groups[1].Value);
            case "pagerduty":
                using(var pd=JsonDocument.Parse(Required("//script[@id='data']").InnerText))
                {
                    if(pd.RootElement.TryGetProperty("summary",out var summary)) return Worst(Items(Field(summary,"open_incidents")).SelectMany(i=>Items(Field(i,"updates")).Take(1)).Select(i=>Map(Text(i,"severity"))),true);
                    var sp=Field(Field(Field(pd.RootElement,"layout"),"layout_settings"),"statusPage");
                    return Field(sp,"globalStatusHeadlineHasError").GetBoolean()?ServiceStatus.Degraded:ServiceStatus.Operational;
                }
            case "statuscast": return Worst(Nodes(ClassXPath("status-list-component-status-text")).Select(n=>Regex.Match(Classes(n),"component-(available|unavailable|informational|monitored|identified|investigating|degraded|maintenance)").Groups[1].Value).Select(Map));
            case "statuspal":
                var cls=Classes(Required(ClassXPath("system-status")));var pal=Regex.Match(cls,"type-(none|minor|major|scheduled)");
                if(!pal.Success) throw new InvalidDataException("Unknown Statuspal summary.");return Map(pal.Groups[1].Value);
            case "beanstalk": return Map(Content(Required("//*[@id='updates']//article[1]//*[contains(@class,'status')]")));
            case "fastly":
                return Worst(Nodes("//tr[@data-path and not(contains(@data-path,'-'))]/td[3]//i").Select(n=>Regex.Match(Classes(n),"(?:component-|status-|icon-)(available|informational|maintenance|degraded|unavailable|investigating|identified|monitoring)").Groups[1].Value).Select(Map));
            case "okta":
                using(var okta=JsonDocument.Parse(Content(Required("//*[@data-id='incidents']"))))
                    return Worst(Items(okta.RootElement).Where(i=>Text(i,"Status__c")!="Resolved").Select(i=>Text(i,"Category__c") switch{
                        "Service Disruption" or "Major Service Disruption" or "Minor Service Disruption"=>ServiceStatus.Outage,
                        "Service Degradation" or "Performance Issue" or "Feature Disruption"=>ServiceStatus.Degraded,_=>throw new InvalidDataException("Unknown Okta incident.")}),true);
            case "uptime":
                var up=Regex.Match(payload,"\"global_is_operational\"\\s*:\\s*(true|false)");
                if(!up.Success) throw new InvalidDataException("Missing Uptime.com summary.");return up.Groups[1].Value=="true"?ServiceStatus.Operational:ServiceStatus.Outage;
            default: throw new InvalidDataException("Unsupported HTML adapter.");
        }
    }
}
