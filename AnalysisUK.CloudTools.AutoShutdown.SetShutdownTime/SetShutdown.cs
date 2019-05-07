using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Lambda.Core;
using Amazon.Lambda.SNSEvents;
using CloudTools.AutoShutdown.SetShutdownTime.Dto;
using Newtonsoft.Json;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace CloudTools.AutoShutdown.SetShutdownTime
{
    /// <summary>
    /// Receives notification via SNS that an instance
    /// should be marked for shutdown in n minutes time.
    ///
    /// Expected to be triggered by OnStart function, however SNS messages may be published by other sources to extend
    /// or reduce the shutdown time.
    /// </summary>
    public class SetShutdown
    {
        private static readonly JsonSerializer JsonSerializer = new JsonSerializer();

        private IAmazonEC2 _client;

        #region Constructors

        public SetShutdown()
        {
            _client = new AmazonEC2Client();
        }

        public SetShutdown(IAmazonEC2 client)
        {
            _client = client;
        }

        #endregion

        /// <summary>
        /// A simple function that takes a string and does a ToUpper
        /// </summary>
        /// <param name="input"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        public async Task FunctionHandler(SNSEvent snsEvent, ILambdaContext context)
        {
            foreach (var snsEventRecord in snsEvent.Records)
            {
                await ProcessSnsEvent(snsEventRecord, context);
            }
        }

        private async Task ProcessSnsEvent(SNSEvent.SNSRecord snsEventRecord, ILambdaContext context)
        {
            var instanceShutdownDto = Deserialize(snsEventRecord);
            await UpdateInstanceTags(instanceShutdownDto, context);
        }

        private static SetInstanceShutdownDto Deserialize(SNSEvent.SNSRecord snsEventRecord)
        {
            using (TextReader textReader = new StringReader(snsEventRecord.Sns.Message))
            {
                using (JsonReader reader = new JsonTextReader(textReader))
                {
                    return JsonSerializer.Deserialize<SetInstanceShutdownDto>(reader);
                }
            }
        }

        private async Task UpdateInstanceTags(SetInstanceShutdownDto instanceShutdownDto, ILambdaContext context)
        {
            var shutdownTime = DateTime.UtcNow.AddMinutes(instanceShutdownDto.StopAfterMinutes);

            CreateTagsRequest request = new CreateTagsRequest
            {
                Resources = new List<string> {instanceShutdownDto.InstanceId},
                Tags = new List<Tag>
                {
                    new Tag("ShutdownAfter", shutdownTime.ToString("O"))
                }
            };

            context.Logger.LogLine($"Setting shutdown time for instance: {instanceShutdownDto.InstanceId} as {shutdownTime.ToString("O")}");

            await _client.CreateTagsAsync(request);

            context.Logger.LogLine($"ShutdownAfter tag set.");
        }
    }
}
