using System;
using Amazon.EC2;

namespace CloudTools.AutoShutdown.Shutdowner.Dto
{
    public class InstanceShutdownDto
    {
        public string InstanceId { get; set; }

        /// <summary>
        /// True if the instance was terminated, false if the instance was simply stopped.
        /// </summary>
        public bool Terminated { get; set; }

        public DateTime LaunchTime { get; set; }
        public double OnDuration { get; set; }
        public string Name { get; set; }
        public InstanceType InstanceType { get; set; }
    }
}