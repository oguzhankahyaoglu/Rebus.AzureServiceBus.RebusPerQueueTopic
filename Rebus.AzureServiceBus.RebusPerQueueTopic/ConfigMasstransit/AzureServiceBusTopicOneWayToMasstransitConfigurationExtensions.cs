using System.Threading;
using Azure.Core;
using Rebus.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus.NameFormat;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigMasstransit;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.Logging;
using Rebus.Subscriptions;
using Rebus.Threading;
using Rebus.Timeouts;
using Rebus.Topic;
using Rebus.Transport;

// ReSharper disable ArgumentsStyleNamedExpression
// ReSharper disable ArgumentsStyleLiteral
// ReSharper disable once CheckNamespace

namespace Rebus.Config
{
    /// <summary>
    /// Configuration extensions for the Azure Service Bus transport
    /// </summary>
    public static class AzureServiceBusTopicOneWayToMasstransitConfigurationExtensions
    {
        const string AsbSubStorageText =
            "The Azure Service Bus transport was inserted as the subscriptions storage because it has native support for pub/sub messaging";

        const string AsbTimeoutManagerText =
            "A disabled timeout manager was installed as part of the Azure Service Bus configuration, becuase the transport has native support for deferred messages";

        /// <summary>
        /// Configures Rebus to use Azure Service Bus Topic to SEND/PUBLISH ONLY to transport messages, connecting to the service bus instance pointed to by the connection string
        /// (or the connection string with the specified name from the current app.config)
        /// </summary>
        public static AzureServiceBusTopicTransportSettings UseAzureServiceBusTopicOneWayToMasstransit(this StandardConfigurer<ITransport> configurer,
            string connectionString, string topicName,
            RebusAzureServiceBusSettings retrySettings,
            TokenCredential tokenCredential = null)
        {
            configurer.OtherService<Options>().Decorate(c =>
            {
                var options = c.Get<Options>();
                options.ExternalTimeoutManagerAddressOrNull = AzureServiceBusQueueTransport.MagicDeferredMessagesAddress;
                return options;
            });
            var settingsBuilder = new AzureServiceBusTopicTransportSettings();

            // register the actual transport as itself
            configurer
                .OtherService<AzureServiceBusTopicOneWayToMasstransitTransport>()
                .Register(c =>
                {
                    var nameFormatter = c.Get<INameFormatter>();
                    var cancellationToken = c.Get<CancellationToken>();
                    var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                    var asyncTaskFactory = c.Get<IAsyncTaskFactory>();
                    var transport = new AzureServiceBusTopicOneWayToMasstransitTransport(
                        connectionString: connectionString,
                        topicName: topicName,
                        rebusLoggerFactory: rebusLoggerFactory,
                        asyncTaskFactory: asyncTaskFactory,
                        nameFormatter: nameFormatter,
                        cancellationToken: cancellationToken,
                        tokenCredential: tokenCredential,
                        settings: settingsBuilder
                    );

                    return transport;
                });

            // map ITransport to transport implementation
            configurer.Register(c => c.Get<AzureServiceBusTopicOneWayToMasstransitTransport>());

            // map subscription storage to transport
            configurer
                .OtherService<ISubscriptionStorage>()
                .Register(c => c.Get<AzureServiceBusTopicOneWayToMasstransitTransport>(), description: AsbSubStorageText);

            // disable timeout manager
            configurer.OtherService<ITimeoutManager>().Register(c => new DisabledTimeoutManager(), description: AsbTimeoutManagerText);

            configurer.OtherService<INameFormatter>().Register(c =>
            {
                // lazy-evaluated setting because the builder needs a chance to be built upon before getting its settings
                return new DefaultNameFormatter();
            });

            configurer.OtherService<DefaultAzureServiceBusTopicNameConvention>().Register(c =>
            {
                var transport = c.Get<AzureServiceBusTopicOneWayToMasstransitTransport>();
                // lazy-evaluated setting because the builder needs a chance to be built upon before getting its settings
                return new DefaultAzureServiceBusTopicNameConvention(transport.Address);
            });

            configurer.OtherService<ITopicNameConvention>().Register(c => c.Get<DefaultAzureServiceBusTopicNameConvention>());

            AzureRebusCommon.RegisterSteps<AzureServiceBusTopicOneWayToMasstransitTransport>(
                configurer, retrySettings, false);
            return settingsBuilder;
        }
    }
}