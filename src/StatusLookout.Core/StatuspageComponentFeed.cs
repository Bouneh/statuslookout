namespace StatusLookout.Core;

/// <summary>Some compatibility summaries omit components. Fetch their complete dedicated feed.</summary>
public static class StatuspageComponentFeed
{
    public static bool Applies(ServiceDefinition service) => OpenAiComponents.Applies(service)
        || service.Provider == "statuspage" && service.ApiUrl.TrimEnd('/') == "https://status.supabase.com/api/v2/summary.json";
    public static string Url(ServiceDefinition service) => new Uri(new Uri(service.ApiUrl.TrimEnd('/')), "components.json").AbsoluteUri;
}
