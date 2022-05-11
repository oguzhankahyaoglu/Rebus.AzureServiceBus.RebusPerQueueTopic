# Rebus.AzureServiceBus.RebusPerQueueTopic

Forked from the original **Rebus.AzureServiceBus** repository. [Rebus.AzureServiceBus](https://github.com/rebus-org/Rebus.AzureServiceBus).

[![install from nuget](https://img.shields.io/nuget/v/Rebus.AzureServiceBus.RebusPerQueueTopic.svg?style=flat-square)](https://www.nuget.org/packages/Rebus.AzureServiceBus.RebusPerQueueTopic)

# How to use?


Install following packages:
- Rebus 6.6.2
- Rebus.ServiceProvider.Named 0.3.0
- Rebus.Serilog 6.0.0

You should not upgrade to newer versions since we created this package in a hurry, compatibility issues were not fixed. Once done, we will update the documentation and the package.

**In startup.cs enable the middleware:**
````
        public static void Configure(IApplicationBuilder app)
        {
            RebusPerQueueTopic.Configure(app);
        }
````
**Create a class containing all topic/queue subscription definitons named "AzureServiceBusConfigurator" ie.**
````
    public static class AzureServiceBusConfigurator
    {
        public static void Register(IServiceCollection services,
            IConfiguration configuration,
            IWebHostEnvironment webHostEnvironment)
        {
            var serviceBusConnectionString = configuration.GetValue<string>("ServiceBus:ConnectionString");
            var r = new RebusPerQueueTopic(services, webHostEnvironment, serviceBusConnectionString, true,
                new RebusAzureServiceBusSettings
                {
                    Environment = webHostEnvironment,
                    RetryDelays = new[]
                    {
                        TimeSpan.FromMinutes(1),
                        TimeSpan.FromMinutes(5),
                    }
                });
````
and call it in your startup.cs while building services.
**Retry Delays** will be used in case a message handling is failed, it will be redelivered to the queue/topic subscription and will be retried. Once maximum retries achieved, it would be dead-lettered. 
Dont forget that **maximum execution time** for a message in Azure ServiceBus is limited with **5 minutes** at most. You can delay a message to tomorrow maybe, but execution time (peek-locking) cannot exceed 5 minutes.

In **AzureServiceBusConfigurator.cs** file, here are some examples:
## Queue Messaging:
Definining a classic queue subscription:
````
            r.Queue<QueueTestMessage2, QueueTestMessage2Handler>("QueueTestMessage2");
            //or one way: term one-way meaning the app itself will not consume the message, will only send it
            r.QueueOneWay<QueueTestOneWayMessage>("QueueOneWayMessage");
            //or to a masstransit consumer
            r.QueueOneWayToMasstransit<MyMasstransitEvent>("MyMasstransitEvent");
````
Example Queue message:
````
  public class QueueTestMessage2
    {
        public string Source { get; set; }
        public string Date { get; set; }
        ...
    }

````
Example message handler:
````
    public class QueueTestMessage2Handler : IHandleMessages<QueueTestMessage2>
    {
        public Task Handle(QueueTestMessage2 message)
        {
            return Task.CompletedTask;
        }
    }
````

## Topic Messaging:
````
    // last bool parameter of true will result in multiple node execution
    //Topictest/Sub1-{{Environment.MachineName}} will be used     
    r.Topic<TopicTestMessage, TopicTestMessageHandler>("TopicTest", "Sub1", true);
    
    //One way topic message sending    
    r.TopicOneWay<TopicTestOneWayMessage>("TopicTest");
    //To masstransit message
    r.TopicOneWayToMasstransit<MasstransitTopicTestMessage>("TopicMasstransitTest");
````

## Health checking:

In "AzureServiceBusConfigurator.cs" lastly, you can optionally enable health checking:
````
    HealthChecksConfigurer.AddBusHealthChecks(services, webHostEnvironment, serviceBusConnectionString,
        new BusHealthCheckOptions
        {
            ExpireConditionDelay = (message,
                messageType) => TimeSpan.FromMinutes(1),
            WarmupIgnoreDelayForHealthChecking = TimeSpan.FromSeconds(15)
        });

````
- **ExpireConditionDelay**: **Default 1 hour.** If any Rebus message (only consumed by this library, ignoring one-way client) creation date is older than 1 hour, the health check will fail.
You can adjust it according to your application, longer/shorter durations might fit your needs.

- **WarmupIgnoreDelayForHealthChecking**: In initial warmup phase of the application like first 1-2 minutes, you might ignore health check errors. Think about a scenario, your queue is full of hundreds message and it will take 30 mins to be consumed. In this scenario, you should ignore health checking due to the warmup phase. 
**Default value is 1 hour.** 

The application will result as healthy by default for the first 1 hour of start. Can be adjusted according to your needs.

# Why forked? What we actually needed?

We switched from Masstransit to Rebus and here were the requirements for us:
- We were using **Queue per message type** and **Topic per message type** on Masstransit
- We have different agile teams, so library-intercommunitication was a must (Rebus-> Masstransit or Masstransit -> Rebus)
- We must have full control on both **queue, topic and topic subscriber names**
- Even after a topic/queue subscription fails, other handlers/busses must be working fine and the faulted one must be restarted.
- A health check mechanism like if a message still exists in a queue/topic more than 1 hour, it must be an alert for us, will result in a failing health check

# Why we switched from Masstransit?

We have strict internal Information Security policies even on Azure Cloud. While we were still on Masstransit, randomly message consumers stopped day by day and Masstransit will not resubscribe to the queue. 

We tried to find the root cause since there were no missing network packages (from Network department), no Firewall issues, not an even debug-level log on production systems relevant with the issue.

However, **we started starting our workdays seeing that a random queue/topic subscriber stopped working** each day. After spending 2 weeks, our team decided to change the messaging infrastructure to another library: Rebus.

# Why the Original Rebus.AzureServiceBus package was not sufficient?

The original implementation of **Rebus.AzureServiceBus** package for Azure Service Bus **Queues** was like this:

````
Configure.With(...)
.Transport(t => t.UseAzureServiceBus(connectionString, "queue")
.AutomaticallyRenewPeekLock())
.(...)
````
and without **Rebus.ServiceProvider.Named** package, you cannot start multiple bus instances.

In original implementation, you specify a ASB connection string and a queue name which Rebus will use. However, you can send many T message types **to only one queue** in this implementation.



**Coming to Topic usages, it is very far away what we expected:**

As you can see in the documentation (https://github.com/rebus-org/Rebus/wiki/Azure-Service-Bus-transport), the implementation does the following internally:
- Create a topic using T Message's full type name (like ProjectName.Events.MyCustomEvent **but we need full control on naming since we have agreements with the other development teams**, and changing all their implementations is not that easy ) 
- Create a topic subscriber using project name (Why? we even run the same application on multiple nodes, targeting the same **Topic** and lets say if we run the app on 5 nodes, sometimes we need a topic message consumed by all the 5 nodes; we were achieving it by creating different topic subscriber names, 5 subscribers on that topic)
- Create a queue with the same name
- Forward topic subscriber to the newly created queue
- Subscribe the queue

**Considering the limitation of Azure Service Bus itself; we cannot start a queue and a topic if they have the same name, it will result in a not allowed exception if you try to create a topic named "Mytopic" but if "MyTopic" named queue exists, it will fail**

Using these conditions, we ensured that the original package will not fit our requirements, but we were on an alarm state and have to change from Masstransit to Rebus as soon as possible, so we re-implemented it.

# What this package does/allows?

1- Minimal usage for topic/queues even in one-way/two-way communication between Rebus/Masstransit libraries

2- Skipped implementation of Sagas, this library is working statelessly.

3- Each topic/queue subscription is living on its own Rebus instance

4- Health checking of each topic/queue subscription

5- Full control on Queue/Topic/Topic Subscription naming

6- For development environment you have an option of: you can prefix all queue/topic names starting with the machine name; this enables me to seperate my queues and application from another guy working on the same project since my queues are created like "oguzhan/queue1" whereas the other guy's "otherguy/queue1"

7- For topic subscribing, you have the option of whether you will need multiple node subscription or not. If enabled, for topic "Topic1" and subscription name of "sub", it will create "Topic1/sub-node1" whereas "node1" is the docker container name.

8- In masstransit you have the limitation of each message is transported with its full namespace. If you move its namespace or change class name, MT will not consume the message. This was an annoying issue for all of us in the company. We are free from this issue thanks to Rebus.

---

# Limitations
- We are working with .NET31, .NET5 and .NET6 so earlier versions are not tested, compatibility issues might be faced.
- We are bound to Rebus.Serilog (< 7.0.0), Rebus.ServiceProvider.Named (0.3.0) and Rebus (<= 6.6.2). When we upgraded the package, we had compatibility issues resulting in application startup crash. 
- Rebus itself has a mechanism of starting multiple busses by keeping a single bus primary, but not tested. Once we are sure about the functionaliy, we will remove Rebus.ServiceProvider.Named package which enabled us to run multiple busses.


---

![](https://raw.githubusercontent.com/rebus-org/Rebus/master/artwork/little_rebusbus2_copy-200x200.png)

---
