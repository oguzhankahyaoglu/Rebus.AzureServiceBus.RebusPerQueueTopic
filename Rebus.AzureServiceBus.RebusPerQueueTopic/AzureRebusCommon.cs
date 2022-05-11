using System;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Steps;
using Rebus.Config;
using Rebus.Transport;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic
{
    internal static class AzureRebusCommon
    {
        public static JsonSerializerSettings NewtonsoftJsonSettings =>
            new()
            {
                TypeNameHandling = TypeNameHandling.None,
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            };

        /// <summary>
        /// Deferred retry'ı zaten yönettiğimiz için buna pek gerek kalmayacak
        /// </summary>
        public static Func<ServiceBusRetryOptions> DefaultRetryStrategy;

        public static void RegisterSteps<TTransport>(StandardConfigurer<ITransport> configurer,
            RebusAzureServiceBusSettings settings)
            where TTransport : ITransport
        {
            configurer.OtherService<RebusAzureServiceBusSettings>()
                .Register(c=> settings);

            DefaultRetryStrategy = () =>
                new()
                {
                    Mode = ServiceBusRetryMode.Fixed,
                    Delay = TimeSpan.FromMinutes(2),
                    MaxDelay = TimeSpan.FromMinutes(5),
                    MaxRetries = 1
                };
            AzureServiceBusStepRegistrar.Register<TTransport>(configurer);
            configurer.UseNativeDeadlettering();
        }
    }
}