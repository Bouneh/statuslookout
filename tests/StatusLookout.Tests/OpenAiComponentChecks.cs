using System.Net;
using StatusLookout.Core;
internal static class OpenAiComponentChecks
{
    private const string Summary="""{"status":{"indicator":"minor","description":"Partial degradation"},"components":[{"id":"responses","name":"Responses","status":"operational"}],"incidents":[]}""";
    private const string Components="""{"components":[{"id":"responses","name":"Responses","status":"operational"},{"id":"codex","name":"Codex Web","status":"degraded_performance"}]}""";
    public static async Task Run(Action<string,Action> check,Func<string,Func<Task>,Task> checkAsync)
    {
        var openai=ServiceCatalog.Load().Single(s=>s.Id=="extra.OpenAI");
        void Assert(bool condition){if(!condition)throw new Exception("OpenAI component assertion failed.");}
        check("OpenAI catalog exposes its component-compatible API",()=>Assert(OpenAiComponents.Applies(openai)));
        check("OpenAI full component feed includes products omitted from its summary",()=>{
            var merged=OpenAiComponents.Merge(Summary,Components);
            Assert(StatusParser.Parse(openai with {Components=["codex"]},merged).Status==ServiceStatus.Degraded);
            Assert(StatusParser.Parse(openai with {Components=["responses"]},merged).Status==ServiceStatus.Operational);
        });
        await checkAsync("OpenAI picker and scoped monitoring share the full component source",async ()=>{
            using var handler=new Handler();using var http=new HttpClient(handler);var monitor=new StatusMonitor(http);
            var choices=await monitor.GetComponentsAsync(openai);Assert(choices.Any(c=>c.Name=="Codex Web"));
            var results=await monitor.CheckAsync([openai with {Id="api",Components=["responses"]},openai with {Id="codex",Components=["codex"]}]);
            Assert(results.Single(s=>s.ServiceId=="api").Status==ServiceStatus.Operational&&results.Single(s=>s.ServiceId=="codex").Status==ServiceStatus.Degraded);
            Assert(handler.SummaryRequests==1&&handler.ComponentRequests==2);
        });
        await checkAsync("An unavailable OpenAI component feed cannot look healthy",async ()=>{
            using var handler=new Handler{FailComponents=true};using var http=new HttpClient(handler);
            var result=(await new StatusMonitor(http).CheckAsync([openai with {Components=["responses"]}])).Single();Assert(result.Status==ServiceStatus.Unknown);
        });
        await checkAsync("OpenAI aggregate checks do not require an extra component request",async ()=>{
            using var handler=new Handler{FailComponents=true};using var http=new HttpClient(handler);
            var result=(await new StatusMonitor(http).CheckAsync([openai])).Single();Assert(result.Status==ServiceStatus.Degraded&&handler.ComponentRequests==0);
        });
    }
    private sealed class Handler : HttpMessageHandler
    {
        public bool FailComponents {get;set;}
        public int SummaryRequests {get;private set;}
        public int ComponentRequests {get;private set;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            var components=request.RequestUri!.AbsoluteUri==OpenAiComponents.ComponentsUrl;
            if(components)ComponentRequests++;else SummaryRequests++;
            return Task.FromResult(new HttpResponseMessage(components&&FailComponents?HttpStatusCode.ServiceUnavailable:HttpStatusCode.OK){Content=new StringContent(components?Components:Summary)});
        }
    }
}
