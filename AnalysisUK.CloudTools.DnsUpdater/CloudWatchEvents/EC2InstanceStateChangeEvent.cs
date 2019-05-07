using Amazon.Lambda.CloudWatchEvents;

namespace AnalysisUK.CloudTools.DnsUpdater.CloudWatchEvents
{
    public class EC2InstanceStateChangeEvent : CloudWatchEvent<EC2InstanceDetail>
    {  }
}