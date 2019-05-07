namespace CloudTools.AutoShutdown.OnStart.Dto
{
    public class SetInstanceShutdownDto
    {
        public string InstanceId { get; set; }
        public int StopAfterMinutes { get; set; }
    }
}