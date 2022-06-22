using System.Threading;
using Azure.Core;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus.NameFormat;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Subscriptions;
using Rebus.Threading;
using Rebus.Timeouts;
using Rebus.Topic;
using Rebus.Transport;

// ReSharper disable ArgumentsStyleNamedExpression
// ReSharper disable ArgumentsStyleLiteral
namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics
{
    /// <summary>
    /// Configuration extensions for the Azure Service Bus transport
    /// </summary>
    public static class AzureServiceBusTopicConfigurationExtensions
    {
        const string AsbSubStorageText =
            "The Azure Service Bus transport was inserted as the subscriptions storage because it has native support for pub/sub messaging";

        const string AsbTimeoutManagerText =
            "A disabled timeout manager was installed as part of the Azure Service Bus configuration, becuase the transport has native support for deferred messages";

        /// <summary>
        /// Configures Rebus to use Azure Service Bus Topic to transport messages, connecting to the service bus instance pointed to by the connection string
        /// (or the connection string with the specified name from the current app.config)
        /// </summary>
        public static AzureServiceBusTopicTransportSettings UseAzureServiceBusTopic(this StandardConfigurer<ITransport> configurer,
            string connectionString,
            string topicName,
            string subscriptionName,
            RebusAzureServiceBusSettings retrySettings,
            bool compress,
            TokenCredential tokenCredential = null)
        {
            var settingsBuilder = new AzureServiceBusTopicTransportSettings
            {
                MaxDeliveryCount = retrySettings.RetryDelays.Length
            };
            // register the actual transport as itself
            configurer
                .OtherService<AzureServiceBusTopicTransport>()
                .Register(c =>
                {
                    var nameFormatter = c.Get<INameFormatter>();
                    var cancellationToken = c.Get<CancellationToken>();
                    var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                    var asyncTaskFactory = c.Get<IAsyncTaskFactory>();

                    var transport = new AzureServiceBusTopicTransport(
                        connectionString: connectionString,
                        topicName: topicName,
                        subscriptionName: subscriptionName,
                        rebusLoggerFactory: rebusLoggerFactory,
                        asyncTaskFactory: asyncTaskFactory,
                        nameFormatter: nameFormatter,
                        cancellationToken: cancellationToken,
                        tokenCredential: tokenCredential,
                        settings: settingsBuilder
                    );

                    if (settingsBuilder.PrefetchingEnabled)
                    {
                        transport.PrefetchMessages(settingsBuilder.NumberOfMessagesToPrefetch);
                    }

                    return transport;
                });

            // map ITransport to transport implementation
            configurer.Register(c => c.Get<AzureServiceBusTopicTransport>());

            // map subscription storage to transport
            configurer
                .OtherService<ISubscriptionStorage>()
                .Register(c => c.Get<AzureServiceBusTopicTransport>(), description: AsbSubStorageText);

            // disable timeout manager
            configurer.OtherService<ITimeoutManager>().Register(c => new DisabledTimeoutManager(), description: AsbTimeoutManagerText);

            configurer.OtherService<INameFormatter>().Register(c =>
            {
                // lazy-evaluated setting because the builder needs a chance to be built upon before getting its settings
                return new DefaultNameFormatter();
            });

            configurer.OtherService<DefaultAzureServiceBusTopicNameConvention>().Register(c =>
            {
                var transport = c.Get<AzureServiceBusTopicTransport>();
                // lazy-evaluated setting because the builder needs a chance to be built upon before getting its settings
                return new DefaultAzureServiceBusTopicNameConvention(transport.Address);
            });

            configurer.OtherService<ITopicNameConvention>().Register(c => c.Get<DefaultAzureServiceBusTopicNameConvention>());

            // remove deferred messages step
            AzureRebusCommon.RegisterSteps<AzureServiceBusTopicTransport>(
                configurer ,retrySettings,compress);
            return settingsBuilder;
        }
    }
}