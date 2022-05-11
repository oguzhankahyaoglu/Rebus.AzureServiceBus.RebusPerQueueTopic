using System;
using Fernas.Business.ServiceBus;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.AzureServiceBus.RebusPerQueueTopic.HealthChecks;
using ServiceBusConfigurator.TestWeb.Messaging;
using TeiasOsosApi.Integration.Events;

namespace ServiceBusConfigurator.TestWeb.StartupConfiguration
{
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
            r.Queue<QueueTestLongRunningMessage, QueueTestLongRunningMessageHandler>(QueueTestLongRunningMessage.QUEUE_NAME);
            //Queues
            r.Queue<YtbsResultCommand, YtbsResultHandler>(YtbsResultCommand.QUEUE_NAME);
            r.Queue<QueueTestMessage, QueueTestMessageHandler>(QueueTestMessage.QUEUE_NAME);
            r.Queue<QueueTestMessage2, QueueTestMessage2Handler>(QueueTestMessage2.QUEUE_NAME);
            //oneway
            r.QueueOneWay<QueueTestOneWayMessage>(QueueTestOneWayMessage.QUEUE_NAME);
            r.QueueOneWayToMasstransit<TeiasOsosApiDataChangedEvent>(TeiasOsosApiDataChangedEvent.QUEUE_NAME);
            r.QueueOneWayToMasstransit<MasstransitQueueTestMessage>(MasstransitQueueTestMessage.QUEUE_NAME);

            r.Queue<YtbsPublishCommand, YtbsPublishCommandHandler>(YtbsPublishCommand.QUEUE_NAME);
            r.Queue<MasstransitToRebusQueueMessage, MasstransitToRebusQueueMessageHandler>(MasstransitToRebusQueueMessage.QUEUE_NAME);

            //Topics
            var subscriberName = "fernas";
            r.Topic<TopicTestMessage, TopicTestMessageHandler>(TopicTestMessage.QUEUE_NAME, subscriberName, true);
            // r.Topic<TopicTestMessage,TopicTestMessageHandler>(TopicTestMessage.QUEUE_NAME, "fernas2");

            r.Topic<TopicTestErrorMessage, TopicTestErrorMessageHandler>(TopicTestErrorMessage.QUEUE_NAME, subscriberName, false);
            //oneway
            r.TopicOneWay<TopicTestOneWayMessage>(TopicTestOneWayMessage.QUEUE_NAME);
            r.TopicOneWayToMasstransit<MasstransitTopicTestMessage>(MasstransitTopicTestMessage.QUEUE_NAME);

            HealthChecksConfigurer.AddBusHealthChecks(services, webHostEnvironment, serviceBusConnectionString,
                new BusHealthCheckOptions
                {
                    ExpireConditionDelay = (message,
                        messageType) => TimeSpan.FromMinutes(1),
                    WarmupIgnoreDelayForHealthChecking = TimeSpan.FromSeconds(15)
                });
        }


        public static void Configure(IApplicationBuilder app)
        {
            RebusPerQueueTopic.Configure(app);
        }

        private static string GetSubscriptionName<T>()
        {
            //var subscriptionName = parameters.ProjectRootNamespace;
            var subscriptionName = "fernas";
            // if (attribute.AllowSimultaneousProcessing)
            // {
            //     var machineName = Environment.MachineName;
            //     machineName = machineName.Replace("/", "-").Replace(".", "-");
            //     subscriptionName = $"{subscriptionName}-{machineName}";
            // }
            return subscriptionName + "~~" + TopicTestMessage.QUEUE_NAME;
        }
    }
}