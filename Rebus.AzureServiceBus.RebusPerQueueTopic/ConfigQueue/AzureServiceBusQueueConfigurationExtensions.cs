using System;
using System.Threading;
using Azure.Core;
using Rebus.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus.NameFormat;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Serialization;
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
    public static class AzureServiceBusQueueConfigurationExtensions
    {
        const string AsbSubStorageText =
            "The Azure Service Bus transport was inserted as the subscriptions storage because it has native support for pub/sub messaging";

        const string AsbTimeoutManagerText =
            "A disabled timeout manager was installed as part of the Azure Service Bus configuration, becuase the transport has native support for deferred messages";

        /// <summary>
        /// Configures Rebus to use Azure Service Bus queues to transport messages, connecting to the service bus instance pointed to by the connection string
        /// (or the connection string with the specified name from the current app.config)
        /// </summary>
        public static AzureServiceBusQueueTransportSettings UseAzureServiceBusQueue(this StandardConfigurer<ITransport> configurer,
            string connectionString,
            string inputQueueAddress,
            RebusAzureServiceBusSettings settings,
            bool compress,
            TokenCredential tokenCredential = null)
        {
            var settingsBuilder = new AzureServiceBusQueueTransportSettings
            {
                MaxDeliveryCount = settings.RetryDelays.Length
            };
            // register the actual transport as itself
            configurer
                .OtherService<AzureServiceBusQueueTransport>()
                .Register(c =>
                {
                    var nameFormatter = c.Get<INameFormatter>();
                    var cancellationToken = c.Get<CancellationToken>();
                    var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                    var asyncTaskFactory = c.Get<IAsyncTaskFactory>();

                    var transport = new AzureServiceBusQueueTransport(
                        connectionString: connectionString,
                        queueName: inputQueueAddress,
                        settings: settingsBuilder,
                        rebusLoggerFactory: rebusLoggerFactory,
                        asyncTaskFactory: asyncTaskFactory,
                        nameFormatter: nameFormatter,
                        cancellationToken: cancellationToken,
                        tokenCredential: tokenCredential
                    );

                    if (settingsBuilder.PrefetchingEnabled)
                    {
                        transport.PrefetchMessages(settingsBuilder.NumberOfMessagesToPrefetch);
                    }

                    return transport;
                });

            RegisterServices(configurer, () => settingsBuilder.LegacyNamingEnabled);

            AzureRebusCommon.RegisterSteps<AzureServiceBusQueueTransport>(
                configurer, settings, compress);
            return settingsBuilder;
        }

        static void RegisterServices(StandardConfigurer<ITransport> configurer,
            Func<bool> legacyNamingEnabled)
        {
            // map ITransport to transport implementation
            configurer.Register(c => c.Get<AzureServiceBusQueueTransport>());

            // map subscription storage to transport
            configurer
                .OtherService<ISubscriptionStorage>()
                .Register(c => c.Get<AzureServiceBusQueueTransport>(), description: AsbSubStorageText);

            // disable timeout manager
            configurer.OtherService<ITimeoutManager>().Register(c => new DisabledTimeoutManager(), description: AsbTimeoutManagerText);

            configurer.OtherService<INameFormatter>().Register(c =>
            {
                // lazy-evaluated setting because the builder needs a chance to be built upon before getting its settings
                var useLegacyNaming = legacyNamingEnabled();

                if (useLegacyNaming) return new LegacyNameFormatter();
                else return new DefaultNameFormatter();
            });

            // configurer.OtherService<DefaultAzureServiceBusTopicNameConvention>().Register(c =>
            // {
            //     // lazy-evaluated setting because the builder needs a chance to be built upon before getting its settings
            //     var useLegacyNaming = legacyNamingEnabled();
            //     return new DefaultAzureServiceBusTopicNameConvention(useLegacyNaming: useLegacyNaming);
            // });
            //
            // configurer.OtherService<ITopicNameConvention>().Register(c => c.Get<DefaultAzureServiceBusTopicNameConvention>());
        }
    }
}