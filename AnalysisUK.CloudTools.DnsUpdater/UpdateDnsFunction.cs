using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Lambda.Core;
using Amazon.Route53;
using Amazon.Route53.Model;
using AnalysisUK.CloudTools.DnsUpdater.CloudWatchEvents;
using AnalysisUK.CloudTools.DnsUpdater.Extensions;

// Assembly attribute to enable the Lambda function's JSON input to be converted into a .NET class.
[assembly: LambdaSerializer(typeof(Amazon.Lambda.Serialization.Json.JsonSerializer))]

namespace AnalysisUK.CloudTools.DnsUpdater
{
    /// <summary>
    /// Update the DNS for a EC2 instance on state change
    /// (i.e. on start - get the new public IP address and update the route
    /// 53 subdomain).
    ///
    /// Needs instances tagged with:
    /// ZoneId: Get this from the Route53 page
    /// HostName: The subdomain name record to modify (it must already exist).
    /// </summary>
    public class UpdateDnsFunction
    {
        private readonly IAmazonEC2 _amazonEc2Client;
        private readonly IAmazonRoute53 _amazonRoute53Client;

        #region Constructors

        public UpdateDnsFunction()
        {
            _amazonEc2Client = new AmazonEC2Client();
            _amazonRoute53Client = new AmazonRoute53Client();
        }

        public UpdateDnsFunction(IAmazonEC2 ec2Client, IAmazonRoute53 route53Client)
        {
            _amazonEc2Client = ec2Client;
            _amazonRoute53Client = route53Client;
        }

        #endregion

        /// <summary>
        /// Lambda function handler.
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
                case "stopping":
                    await RemoveRoute53Entry(cloudWatchEvent, context);
                    break;
                case "running":
                    await UpdateRoute53Entry(cloudWatchEvent, context);
                    break;
                default:
                    context.Logger.LogLine($"No action for state: {cloudWatchEvent.Detail.State}");
                    break;
            }
        }

        private static void LogEvent(EC2InstanceStateChangeEvent cloudWatchEvent, ILambdaContext context)
        {
            context.Logger.LogLine($"Source: {cloudWatchEvent.Source}");
            context.Logger.LogLine($"Region: {cloudWatchEvent.Region}");
            context.Logger.LogLine($"DetailType: {cloudWatchEvent.DetailType}");
            context.Logger.LogLine($"InstanceId: {cloudWatchEvent.Detail.InstanceId}");
            context.Logger.LogLine($"State: {cloudWatchEvent.Detail.State}");
        }

        /// <summary>
        /// Instance is stopping 
        ///
        /// Remove the route 53 entry for it.
        /// </summary>
        /// <param name="cloudWatchEvent"></param>
        /// <param name="context"></param>
        /// <returns></returns>
        private async Task RemoveRoute53Entry(EC2InstanceStateChangeEvent cloudWatchEvent,
            ILambdaContext context)
        {
            Instance instance = await GetInstanceDetails(cloudWatchEvent.Detail.InstanceId, context);

            if (instance != null)
            {
                HostedZone zone = await GetZone(instance, context);

                if (zone == null)
                {
                    return;
                }

                await DeleteRoute53RecordSet(zone, instance, context);
            }
        }

        private async Task DeleteRoute53RecordSet(HostedZone zone, Instance instance, ILambdaContext context)
        {
            context.Logger.LogLine($"**** TODO: Delete route53 record set!");
            // TODO:
        }

        private async Task UpdateRoute53Entry(EC2InstanceStateChangeEvent cloudWatchEvent,
            ILambdaContext context)
        {
            Instance instance = await GetInstanceDetails(cloudWatchEvent.Detail.InstanceId, context);

            if (instance != null)
            {
                HostedZone zone = await GetZone(instance, context);

                await UpdateRoute53(zone, instance, context);
            }
            else
            {
                context.Logger.LogLine($"**** InstanceId Not Found: {cloudWatchEvent.Detail.InstanceId}");
            }
        }

        private async Task<HostedZone> GetZone(Instance instance, ILambdaContext context)
        {
            string zoneId = instance.Tags.GetTag("ZoneId");

            if (string.IsNullOrWhiteSpace(zoneId))
            {
                context.Logger.LogLine($"Zone missing!");
                return null;
            }

            // Get the hosted zone information from route53.
            GetHostedZoneRequest request = new GetHostedZoneRequest(zoneId);
            var response = _amazonRoute53Client.GetHostedZoneAsync(request);

            var hostedZone = response.Result.HostedZone;
            if (hostedZone != null)
            {
                context.Logger.LogLine($"Found hosted zone: {hostedZone.Name}");
            }

            return hostedZone;
        }

        private async Task UpdateRoute53(HostedZone zone, Instance instance, ILambdaContext context)
        {
            if (zone == null)
            {
                return;
            }

            string hostName = instance.Tags.GetTag("HostName");

            if (string.IsNullOrWhiteSpace(hostName))
            {
                context.Logger.LogLine($"Hostname missing!");
                return;
            }

            string ipAddress = instance.PublicIpAddress;
            if (string.IsNullOrWhiteSpace(ipAddress))
            {
                context.Logger.LogLine($"ipAddress missing!");
                return;
            }

            context.Logger.LogLine($"Update Zone: {zone.Name} host: {hostName} with IpAddress: {ipAddress}");

            ChangeResourceRecordSetsRequest request = new ChangeResourceRecordSetsRequest
            {
                HostedZoneId = zone.Id,
                ChangeBatch = new ChangeBatch
                {
                    Changes = new List<Change>
                    {
                        new Change
                        {
                            Action = ChangeAction.UPSERT,
                            ResourceRecordSet = new ResourceRecordSet
                            {
                                Name = $"{hostName}.{zone.Name}",
                                Type = "A",
                                ResourceRecords = new List<ResourceRecord>
                                {
                                    new ResourceRecord {Value = ipAddress}
                                },
                                TTL = 60,
                                
                            }
                        }
                    }
                }
            };
            await _amazonRoute53Client.ChangeResourceRecordSetsAsync(request);
        }

        private async Task<Instance> GetInstanceDetails(string resource, ILambdaContext context)
        {
            DescribeInstancesRequest request = new DescribeInstancesRequest
            {
                InstanceIds = new List<string> { resource},
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
    }
}
