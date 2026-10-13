using System.Text;
using StatusLookout.Core;
static class AwsChecks
{
    public static void Run(Action<string,Action> check)
    {
        void Assert(bool value) { if(!value)throw new Exception("AWS assertion failed."); }
        void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("Invalid AWS response accepted.");}
        var aws=new ServiceDefinition("aws","AWS","AWS services","aws","https://health.aws.amazon.com/health/status","https://health.aws.amazon.com/public/currentevents");
        var incident="""[{"arn":"aws-event","service":"multipleservices-eu-west-3","service_name":"Multiple services","region_name":"Paris","status":"3","summary":"Regional issue","impacted_services":{"s3-eu-west-3":{"service_name":"Amazon Simple Storage Service","current":"3"},"ec2-eu-west-3":{"service_name":"Amazon Elastic Compute Cloud","current":"0"}}}]""";
        check("AWS aggregate recognizes public issues",()=>Assert(StatusParser.Parse(aws,incident).Status==ServiceStatus.Degraded));
        check("AWS unaffected region stays operational",()=>Assert(StatusParser.Parse(aws with{Components=["region:eu-west-1"]},incident).Status==ServiceStatus.Operational));
        check("AWS impacted region is reported",()=>Assert(StatusParser.Parse(aws with{Components=["region:eu-west-3"]},incident).Status==ServiceStatus.Degraded));
        check("AWS filters affected products and respects resolved impacts",()=>
        {
            Assert(StatusParser.Parse(aws with{Components=["product:Amazon Simple Storage Service"]},incident).Status==ServiceStatus.Degraded);
            Assert(StatusParser.Parse(aws with{Components=["product:Amazon Elastic Compute Cloud"]},incident).Status==ServiceStatus.Operational);
        });
        check("AWS product and region scopes intersect",()=>Assert(StatusParser.Parse(aws with{Components=["product:Amazon Simple Storage Service","region:eu-west-1"]},incident).Status==ServiceStatus.Operational));
        check("AWS closed events cannot create an outage",()=>Assert(StatusParser.Parse(aws,incident.Replace("\"status\":\"3\"","\"status\":\"0\"")).Status==ServiceStatus.Operational));
        check("AWS empty feed is valid but wrong schema is rejected",()=>{Assert(StatusParser.Parse(aws,"[]").Status==ServiceStatus.Operational);Reject(()=>StatusParser.Parse(aws,"{}"));Reject(()=>StatusParser.Parse(aws,"[{\"status\":\"3\"}]"));});
        check("AWS identities allow grouping the same regional incident",()=>Assert(StatusParser.Parse(aws,incident).IncidentKey==StatusParser.Parse(aws with{Components=["region:eu-west-3"]},incident).IncidentKey));
        check("AWS UTF16BE responses and UTF8 BOM decode correctly",()=>
        {
            var bytes=Encoding.BigEndianUnicode.GetPreamble().Concat(Encoding.BigEndianUnicode.GetBytes(incident)).ToArray();
            Assert(StatusParser.Parse(aws,StatusPayload.Decode(bytes)).Status==ServiceStatus.Degraded);
            Assert(StatusPayload.Decode(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("[]")).ToArray())=="[]");
        });
        check("AWS catalog contains EC2, S3 and Paris",()=>
        {
            var catalog=ServiceCatalog.Load();Assert(catalog.Any(s=>s.Id=="extra.AWS"));
            Assert(catalog.Any(s=>s.Name.Contains("(EC2)")));Assert(catalog.Any(s=>s.Name.Contains("(S3)")));Assert(catalog.Any(s=>s.Id=="aws.region.eu-west-3"));
        });
        check("Stream Deck can enable only identified catalog services",()=>
        {
            StreamDeckProtocol.Validate(new("watch","extra.AWS"));
            Reject(()=>StreamDeckProtocol.Validate(new("watch")));
        });
    }
}
