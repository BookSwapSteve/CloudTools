using Newtonsoft.Json;

namespace AnalysisUK.CloudTools.DnsUpdater.CloudWatchEvents
{
    public class EC2InstanceDetail
    {
        [JsonProperty(PropertyName = "instance-id")]
        public string InstanceId { get; set; }

        public string State { get; set; }
    }
}