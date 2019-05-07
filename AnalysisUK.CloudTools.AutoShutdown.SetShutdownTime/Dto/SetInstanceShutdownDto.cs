namespace CloudTools.AutoShutdown.SetShutdownTime.Dto
{
    /// <summary>
    /// SNS message.
    /// </summary>
    public class SetInstanceShutdownDto
    {
        public string InstanceId { get; set; }
        public int StopAfterMinutes { get; set; }
    }
}