# CloudTools

A few tools to help with cloud automation.

## DnsUpdater

Run out of Elastic IP Addresses?

Don't want to pay for non-attached Elastic IP Addressed whilst the instance is stopped?

Fed up with having a different IP addresses for an instance when it starts?

DnsUpdater is a AWS Lambda function that set the A record for a zone in route 53 for a EC2 instance when it is started.

### Setup:

* Create a Lambda Function
* Configure a role for the function.
** EC2 Read Access
** Route53 Full Access
** CloudWatch Full Access
* Publish the function to the function
* Head over to CloudWatch -> Rules
* Create a new rule
** Event Pattern
** Service Name: EC2
** Event Type: EC2 Instance State-change Notification
** Any State (Or specific state: running)
** Any Instance (or use specific instances if you with)
* Add a target
** Lambda function
** Select the appropriate Lambda Function
** Use the defaults.
* Click Configure details.

Tag the instances you want to update DNS for.

* Tag: "ZoneId", enter the Route53 zone id (available in the zone list)
* Tag: "HostName", enter the A record name to use. (i.e. if you want staging.foo.com, use staging).

When the Lambda expression receives a running notification from CloudWatch it will update (create or change) the a record for the hostname in the zone.

You may also like to include a DomainName tag to help remember what domain this is under, but it's not used by the function.

### Issues:

* This needs to be configured for each region.
* DNS record is not removed when the instance is stopped - your subdomain will point to somebody elses server! 


## AutoShutdown

This will shutdown an instance automatically a set time after the instance is started.

Example:

You may need a CI server running during work days (or for a few hours for evening projects), after that time it may sit idle, running up costs.

With AutoShutdown the server is automatically stopped after a set number of minutes. (i.e. 600 for work, 120 for home projects, 20 for a short lived experiment).

This saves you manually having to stop the servers at the end of the day, which is easily forgotten, leading to wasted money.

AutoShutdown is made from 3 individual functions, 

* Monitor EC2 instances starting
* Set/Update the shutdown time
* Acutally shutdown the instance.

Tags:

* "StopAfterMinutes" - Value represents the number of minutes after the instance is started when it should be stopped.

### OnStart

Tags:

* "StopAfterMinutes" - Value represents the number of minutes after the instance is started when it should be stopped.

This receives notification from CloudFrot when an EC2 instance changes state (i.e. starts running).

If the required tags are found on the instance a SNS message is published to indicate the time the instance should be shutdown at.

This is then handled by the SetShutdownTime function

The instance must be tagged with StopAfterMinutes and a value for how many minutes after it is started the server should be stopped.


### SetShutdownTime

Subscribes to a SNS topic. This received a notification from OnStart function to set the shutdown time.

The ShutdownAfter tag is assigned to the instance with the date/time the instance should be shutdown at. This is used by the "Shutdowner" function.

You may also send a message on the SNS topic to modify (extend/reduce) the shutdown time.

{
"InstanceId": <i-de543553>,
"StopAfterMinutes": 60
}

This extends the shutdown time by the specified number of minutes. Note that this may result in a faster shutdown time. 

### Shutdowner

This function is responsible for shutting down the EC2 Instance.

This is triggered by a CloudWatch scheduled event. It checks all the instances in the region and looks for the ShutdownAfter tag on running machines.

Tags:

* "ShutdownAfter" - value represents the time after which the instance should be shutdown.
* "ShutdownAction" - represents the action for shutdown. "Stop" or "Terminate". If missing or misconfigured "Stop" is used.

You can manually update the ShutdownAfter tag (or add one) to have Shutdowner shut the machine down.
