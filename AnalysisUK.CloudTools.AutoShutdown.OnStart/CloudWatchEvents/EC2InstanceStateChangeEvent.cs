using Amazon.Lambda.CloudWatchEvents;

namespace CloudTools.AutoShutdown.OnStart.CloudWatchEvents
{
    public class EC2InstanceStateChangeEvent : CloudWatchEvent<EC2InstanceDetail>
    { }
}