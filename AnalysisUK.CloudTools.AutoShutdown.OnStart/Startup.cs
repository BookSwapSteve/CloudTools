using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using CloudTools.AutoShutdown.OnStart.CloudWatchEvents;
using CloudTools.AutoShutdown.OnStart.Dto;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace CloudTools.AutoShutdown.OnStart
{
    /// <summary>
    /// Called by CloudWatch when a EC2 instance state change happens.
    ///
    /// This is used to determine if the instance is tagged for auto-shutdown and hence
    /// to publish a message to a SNS topic for setting the shutdown time on the instance.
    /// </summary>
    public class Startup
    {
        private readonly IAmazonEC2 _amazonEc2Client;
        private readonly IAmazonSimpleNotificationService _snsClient;

        #region Constructors

        public Startup()
        {
            _amazonEc2Client = new AmazonEC2Client();
            _snsClient = new AmazonSimpleNotificationServiceClient();
        }

        public Startup(IAmazonEC2 ec2Client, IAmazonSimpleNotificationService snsClient)
        {
            _amazonEc2Client = ec2Client;
            _snsClient = snsClient;
        }

        #endregion

        /// <summary>
        /// Lambda function handler. Triggered by CloudWatch EC2 instance state change.
        /// </summary>
        /// <param name="cloudWatchEvent"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(EC2InstanceStateChangeEvent cloudWatchEvent, ILambdaContext context)
        {
            context.Logger.LogLine("EC2 State Change event triggered.");

            LogEvent(cloudWatchEvent, context);

            switch (cloudWatchEvent.Detail.State)
            {
                case "running":
                    await SetShutdownTime(cloudWatchEvent, context);
                    break;
                default:
                    context.Logger.LogLine($"No action for state: {cloudWatchEvent.Detail.State}");
                    break;
            }
        }

        private async Task SetShutdownTime(EC2InstanceStateChangeEvent cloudWatchEvent, ILambdaContext context)
        {
            Instance instance = await GetInstanceDetails(cloudWatchEvent.Detail.InstanceId, context);

            var tag = instance.Tags.FirstOrDefault(x => x.Key == "StopAfterMinutes");
            if (tag == null)
            {
                context.Logger.LogLine($"Instance {instance.InstanceId} does not have a 'StopAfterMinutes' tag. Ignoring");
                return;
            }

            if (!int.TryParse(tag.Value, out var minutes))
            {
                context.Logger.LogLine($"Instance {instance.InstanceId} has invalid time for 'StopAfterMinutes' tag. Ignoring");
                return;
            }

            SetInstanceShutdownDto shutdownDto = new SetInstanceShutdownDto {InstanceId = instance.InstanceId, StopAfterMinutes = minutes};
            string message = Newtonsoft.Json.JsonConvert.SerializeObject(shutdownDto);

            var snsTopic = GetSnsTopic();
            context.Logger.LogLine($"Publishing SetInstanceShutdown message to topic {snsTopic}. Message: {message}");

            PublishRequest request = new PublishRequest(snsTopic, message);
            await _snsClient.PublishAsync(request);

            context.Logger.LogLine($"Published!");
        }

        private static void LogEvent(EC2InstanceStateChangeEvent cloudWatchEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Source: {cloudWatchEvent.Source}");
            context.Logger.LogLine($"Region: {cloudWatchEvent.Region}");
            context.Logger.LogLine($"DetailType: {cloudWatchEvent.DetailType}");
            context.Logger.LogLine($"InstanceId: {cloudWatchEvent.Detail.InstanceId}");
            context.Logger.LogLine($"State: {cloudWatchEvent.Detail.State}");
        }

        private async Task<Instance> GetInstanceDetails(string resource, ILambdaContext context)
        {
            DescribeInstancesRequest request = new DescribeInstancesRequest
            {
                InstanceIds = new List<string> { resource },
            };

            DescribeInstancesResponse response = await _amazonEc2Client.DescribeInstancesAsync(request);

            context.Logger.LogLine($"DescribeInstances. Reservations.Count: {response.Reservations.Count}");

            if (response.Reservations.Any())
            {
                var reservation = response.Reservations.First();
                context.Logger.LogLine($"DescribeInstances. ReservationId: {reservation.ReservationId}");
                context.Logger.LogLine($"DescribeInstances. Reservation.Instances.Count: {reservation.Instances.Count}");
                return reservation.Instances.FirstOrDefault();
            }

            return null;
        }

        private static string GetSnsTopic()
        {
            return Environment.GetEnvironmentVariable("TopicArn");
        }
    }

}
