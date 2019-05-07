using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Lambda.Core;
using Amazon.SimpleNotificationService;
using CloudTools.AutoShutdown.Shutdowner.Dto;


// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace CloudTools.AutoShutdown.Shutdowner
{
    public class Shutdown
    {
        private IAmazonEC2 _client;
        private readonly IAmazonSimpleNotificationService _snsClient;

        #region Constructors

        public Shutdown()
        {
            _client = new AmazonEC2Client();
            _snsClient = new AmazonSimpleNotificationServiceClient();
        }

        public Shutdown(IAmazonEC2 client, IAmazonSimpleNotificationService snsClient)
        {
            _client = client;
            _snsClient = snsClient;
        }

        #endregion

        /// <summary>
        /// This function will shutdown instances that are tagged with "ShutdownAfter" and a (UTC) date/time.
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(Amazon.Lambda.CloudWatchEvents.ScheduledEvents.ScheduledEvent scheduledEvent, ILambdaContext context)
        {
            // We don't actually care about the scheduled event, it's just here to trigger the function

            // Note: This only works for the first 1000 instances...
            DescribeInstancesRequest request = new DescribeInstancesRequest()
            {
                MaxResults = 1000,
                // Filter for only those instances that are tagged and are running.
                Filters = new List<Filter>
                {
                    new Filter("tag:ShutdownAfter", new List<string> {"*"}),
                    new Filter("instance-state-name", new List<string> {"running"})
                }
            };
            DescribeInstancesResponse response = await _client.DescribeInstancesAsync(request);

            context.Logger.LogLine($"Found {response.Reservations.Count} tagged instances");

            foreach (var reservation in response.Reservations)
            {
                foreach (var instance in reservation.Instances)
                {
                    await TryShutdownInstance(instance, context);
                }
            }
        }

        private async Task TryShutdownInstance(Instance instance, ILambdaContext context)
        {
            try
            {
                if (IsShutdownDue(instance, context))
                {
                    await ShutdownInstanceAsync(instance, context);
                    await RemoveTagAsync(instance, context);
                }
            }
            catch (Exception ex)
            {
                context.Logger.LogLine($"Error trying to shutdown instance: {instance.InstanceId}");
            }
        }

        private bool IsShutdownDue(Instance instance, ILambdaContext context)
        {
            var tag = instance.Tags.First(x => x.Key == "ShutdownAfter");

            if (string.IsNullOrWhiteSpace(tag.Value))
            {
                return false;
            }

            var shutdownAfter = DateTime.Parse(tag.Value);
            context.Logger.LogLine($"Shutdown after {shutdownAfter} for instance {instance.InstanceId}");

            // If we've gone past the shutdown date, then shutdown the machine.
            return shutdownAfter < DateTime.UtcNow;
        }

        private async Task ShutdownInstanceAsync(Instance instance, ILambdaContext context)
        {
            // It's also possible to hibernate - not supported at this time.
            bool terminate = TerminateOnShutdown(instance);

            if (terminate)
            {
                context.Logger.LogLine($"*** Terminating instance!!! {instance.InstanceId}");
                TerminateInstancesRequest terminateRequest = new TerminateInstancesRequest(new List<string> { instance.InstanceId });
                await _client.TerminateInstancesAsync(terminateRequest);
            }
            else
            {
                context.Logger.LogLine($"*** Stopping instance {instance.InstanceId}");
                StopInstancesRequest request = new StopInstancesRequest(new List<string> { instance.InstanceId });
                await _client.StopInstancesAsync(request);
            }

            await PublishSnsMessageAsync(instance, terminate, context);

            context.Logger.LogLine($"Shutdown for instance {instance.InstanceId} requested.");
        }

        /// <summary>
        /// Clears the ShutdownAfter tagged value to prevent the instance getting shutdown
        /// when the user starts it next time.
        ///
        /// This allows the user to manually enter the tag value without having to remember the tag name.
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task RemoveTagAsync(Instance instance, ILambdaContext context)
        {
            CreateTagsRequest createRequest = new CreateTagsRequest(new List<string> { instance.InstanceId }, new List<Tag> { new Tag("ShutdownAfter", "") });
            await _client.CreateTagsAsync(createRequest);
        }

        private bool TerminateOnShutdown(Instance instance)
        {
            // Get tag "ShutdownAction"
            // return true if present and set to "Terminate" 
            var tag = instance.Tags.FirstOrDefault(x => x.Key == "Terminate");

            if (tag == null)
            {
                return false;
            }

            return "Terminate".Equals(tag.Value, StringComparison.CurrentCultureIgnoreCase);
        }

        /// <summary>
        /// Publish a SNS message to indicate we've shutdown the instance.
        ///
        /// Leave "PublishTopicArn" environment variable empty to skip this.
        ///
        /// Subscribe to the topic via email for an easy email notification
        /// </summary>
        /// <param name="instance"></param>
        /// <param name="terminated"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task PublishSnsMessageAsync(Instance instance, bool terminated, ILambdaContext context)
        {
            string topicArn = GetSnsTopic();
            if (!string.IsNullOrWhiteSpace(topicArn))
            {
                var launchTime = instance.LaunchTime;
                double onTime = DateTime.UtcNow.Subtract(launchTime).TotalMinutes;
                var nameTag = instance.Tags.FirstOrDefault(x => "Name".Equals(x.Key));
                string name = nameTag?.Value;

                InstanceShutdownDto dto = new InstanceShutdownDto
                {
                    InstanceId = instance.InstanceId,
                    Terminated = terminated,
                    LaunchTime = launchTime,
                    OnDuration = onTime,
                    Name = name,
                    InstanceType = instance.InstanceType
                };

                string message = Newtonsoft.Json.JsonConvert.SerializeObject(dto);

                context.Logger.LogLine($"Publishing shutdown message to SNS Topic: {topicArn}");
                await _snsClient.PublishAsync(topicArn, message);
            }
        }

        /// <summary>
        /// Get the SNS topic that is used to send a notification to when we have
        /// shutdown an instance.
        /// </summary>
        /// <returns></returns>
        private static string GetSnsTopic()
        {
            return Environment.GetEnvironmentVariable("PublishTopicArn");
        }
    }
}
