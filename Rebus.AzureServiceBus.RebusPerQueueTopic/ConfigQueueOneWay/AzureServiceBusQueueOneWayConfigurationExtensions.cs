using System;
using System.Threading;
using Azure.Core;
using Rebus.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus.NameFormat;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueueOneWay;
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
    public static class AzureServiceBusQueueOneWayConfigurationExtensions
    {
        const string AsbSubStorageText =
            "The Azure Service Bus transport was inserted as the subscriptions storage because it has native support for pub/sub messaging";

        const string AsbTimeoutManagerText =
            "A disabled timeout manager was installed as part of the Azure Service Bus configuration, becuase the transport has native support for deferred messages";

        /// <summary>
        /// Configures Rebus to use Azure Service Bus to transport messages as a one-way client (i.e. will not be able to receive any messages)
        /// </summary>
        public static AzureServiceBusTransportClientSettings UseAzureServiceBusQueueAsOneWayClient(this StandardConfigurer<ITransport> configurer,
            string connectionString,
            RebusAzureServiceBusSettings retrySettings,
            bool compress,
            TokenCredential tokenCredential = null)
        {
            var settingsBuilder = new AzureServiceBusTransportClientSettings();

            configurer.OtherService<Options>().Decorate(c =>
            {
                var options = c.Get<Options>();
                options.ExternalTimeoutManagerAddressOrNull = AzureServiceBusQueueOneWayTransport.MagicDeferredMessagesAddress;
                return options;
            });
            

            configurer
                .OtherService<AzureServiceBusQueueOneWayTransport>()
                .Register(c =>
                {
                    var cancellationToken = c.Get<CancellationToken>();
                    var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                    var asyncTaskFactory = c.Get<IAsyncTaskFactory>();
                    var nameFormatter = c.Get<INameFormatter>();

                    var transport = new AzureServiceBusQueueOneWayTransport(
                        connectionString: connectionString,
                        rebusLoggerFactory: rebusLoggerFactory,
                        asyncTaskFactory: asyncTaskFactory,
                        nameFormatter: nameFormatter,
                        cancellationToken: cancellationToken,
                        tokenCredential: tokenCredential
                    ) {MaximumMessagePayloadBytes = settingsBuilder.MaximumMessagePayloadBytes};


                    return transport;
                });

            RegisterServices(configurer, () => settingsBuilder.LegacyNamingEnabled);
            AzureRebusCommon.RegisterSteps<AzureServiceBusQueueOneWayTransport>(
                configurer, retrySettings,compress);
            OneWayClientBackdoor.ConfigureOneWayClient(configurer);

            return settingsBuilder;
        }

        static void RegisterServices(StandardConfigurer<ITransport> configurer,
            Func<bool> legacyNamingEnabled)
        {
            // map ITransport to transport implementation
            configurer.Register(c => c.Get<AzureServiceBusQueueOneWayTransport>());

            // map subscription storage to transport
            configurer
                .OtherService<ISubscriptionStorage>()
                .Register(c => c.Get<AzureServiceBusQueueOneWayTransport>(), description: AsbSubStorageText);

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