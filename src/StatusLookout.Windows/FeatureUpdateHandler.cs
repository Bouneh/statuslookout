using System.Net;
using System.Text.Json;
namespace StatusLookout.Windows;

/// <summary>In-memory release metadata for the isolated --feature-test path.</summary>
internal sealed class FeatureUpdateHandler : HttpMessageHandler
{
    public int VersionMajor {get;set;}=90;
    public bool Fail {get;set;}
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)
    {
        if(request.RequestUri?.AbsoluteUri!=StatusLookout.Core.UpdateService.LatestEndpoint)throw new InvalidOperationException("Unexpected test request.");
        if(Fail)return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var tag=$"v{VersionMajor}.0.0";
        var assets=new[]{"win-x64","win-x86","win-arm64"}.Select(runtime=>new {name=$"StatusLookout-{runtime}.exe",browser_download_url=$"https://github.com/Bouneh/statuslookout/releases/download/{tag}/StatusLookout-{runtime}.exe",digest="sha256:"+new string('a',64),size=100});
        var json=JsonSerializer.Serialize(new{draft=false,prerelease=false,tag_name=tag,assets});
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
    }
}
