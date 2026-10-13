using System.Text.Json;
using System.Text.Json.Nodes;
namespace StatusLookout.Core;

/// <summary>OpenAI's compatibility summary omits some components (including Codex).
/// Use the dedicated components feed for discovery and scoped checks.</summary>
public static class OpenAiComponents
{
    public const string SummaryUrl="https://status.openai.com/api/v2/summary.json";
    public const string ComponentsUrl="https://status.openai.com/api/v2/components.json";
    public static bool Applies(ServiceDefinition service)=>service.Provider=="statuspage"&&service.ApiUrl.TrimEnd('/')==SummaryUrl;
    public static string Merge(string summary,string components)
    {
        try
        {
            if(JsonNode.Parse(summary) is not JsonObject root||root["status"] is not JsonObject)
                throw new InvalidDataException("Missing OpenAI status summary.");
            if(JsonNode.Parse(components) is not JsonObject feed||feed["components"] is not JsonArray values||values.Count==0)
                throw new InvalidDataException("Missing OpenAI component status data.");
            root["components"]=values.DeepClone();return root.ToJsonString();
        }
        catch(JsonException ex){throw new InvalidDataException("Invalid OpenAI component response.",ex);}
    }
}
