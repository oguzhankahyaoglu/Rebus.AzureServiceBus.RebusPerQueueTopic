using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.AzureServiceBus.RebusPerQueueTopic.HealthChecks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Providers;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Serializers;
using Rebus.Config;
using Rebus.Handlers;
using Rebus.Retry.Simple;
using Rebus.Routing.TypeBased;
using Rebus.Serialization;
using Rebus.Serialization.Json;
using Rebus.ServiceProvider;
using Rebus.ServiceProvider.Named;
using Serilog;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator
{
    public class RebusPerQueueTopic
    {
        private static readonly ConcurrentBag<Type> RegisteredMessageTypes = new();

        private readonly IServiceCollection _services;
        private readonly string _queueTopicNamePrefix = "";

        private static RebusAzureServiceBusSettings _settings = new()
        {
            RetryDelays = new[]
            {
                // TimeSpan.FromSeconds(5),
                // TimeSpan.FromSeconds(10),
                // TimeSpan.FromSeconds(15),
                TimeSpan.FromMinutes(1),
                TimeSpan.FromMinutes(5),
                // TimeSpan.FromMinutes(30),
            }
        };

        private readonly string _serviceBusConnectionString;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="services"></param>
        /// <param name="env"></param>
        /// <param name="serviceBusConnectionString"></param>
        /// <param name="useDevelopmentModeMachineNamePrefix">True=> machinename prefixed queues/topics for development purposes</param>
        /// <param name="settings"></param>
        public RebusPerQueueTopic(IServiceCollection services,
            IWebHostEnvironment env,
            string serviceBusConnectionString,
            bool useDevelopmentModeMachineNamePrefix,
            RebusAzureServiceBusSettings settings = null)
        {
            _services = services;
            _serviceBusConnectionString = serviceBusConnectionString;

            services.TryAddTransient<IMessageBrokerProvider, RebusProvider>();
            if (settings != null)
            {
                _settings = settings;
            }

            _settings.Environment = env;
            _services.AddSingleton(_settings);
            services.AddMvc()
                .AddApplicationPart(GetType().Assembly)
                .AddControllersAsServices();
            //Not implemented: test etmek lazım güzelce?
            if (useDevelopmentModeMachineNamePrefix)
            {
                _queueTopicNamePrefix = DevelopmentQueueNameHelper.GetPrefix(env);
            }
        }

        void CommonOptions(OptionsConfigurer x)
        {
            x.SetMaxParallelism(1);
            x.SetNumberOfWorkers(1);
            x.SimpleRetryStrategy(maxDeliveryAttempts: 1, secondLevelRetriesEnabled: true);
            // x.SimpleRetryStrategy();
            if (Debugger.IsAttached)
                x.LogPipeline(true);
        }

        private void LogNewLine()
        {
            Log.Information("-----------------------------------");
        }

        private static JsonSerializerSettings GetNewtonsoftJsonSettings =>
            new()
            {
                TypeNameHandling = TypeNameHandling.None,
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore,
                Formatting = Formatting.None
            };

        // private readonly JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions
        // {
        //     IgnoreNullValues = true,
        //     PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        // };

        private Action<StandardConfigurer<ISerializer>> SerializerChoose()
        {
            return s => s.UseNewtonsoftJson(GetNewtonsoftJsonSettings);
        }

        public void Queue<TMessage, TMessageHandler>(string queueName, bool compress = false)
            where TMessage : class
            where TMessageHandler : IHandleMessages, IHandleMessages<TMessage>
        {
            queueName = _queueTopicNamePrefix + queueName;
            RegisteredMessageTypes.Add(typeof(TMessage));
            RebusHealthChecks.RegisteredQueueTopicItems.Add(new HealthCheckQueueTopicItem
            {
                QueueName = queueName,
                MessageType = typeof(TMessage)
            });
            _services.AddRebusHandler<TMessageHandler>();
            _services.AddRebusHandler<AzureDeferSecondLevelRetryHandler<TMessage>>();
            _services.AddTypedRebus<TMessage>(rebus =>
            {
                LogNewLine();
                Log.Information("Registering Queue {messagetype} with handler of {handlertype}", typeof(TMessage).FullName,
                    typeof(TMessageHandler).FullName);
                rebus
                    .Logging(x => x.Serilog(Log.Logger))
                    .Transport(x =>
                    {
                        x.UseAzureServiceBusQueue(_serviceBusConnectionString, queueName, _settings, compress)
                            .SetMessagePayloadSizeLimit(1024 * 1024) //1024 KB
                            .SetAutoDeleteOnIdle(TimeSpan.FromDays(7))
                            .SetMessagePeekLockDuration(TimeSpan.FromMinutes(5))
                            .SetDefaultMessageTimeToLive(TimeSpan.FromDays(1))
                            ;
                    })
                    .Serialization(SerializerChoose())
                    .Options(CommonOptions)
                    .Routing(x =>
                    {
                        x.TypeBased().Map<TMessage>(queueName);
                        //x.TypeBased().Map<IFailed<TMessage>>()
                    })
                    ;

                //ToDo: healthcheck burdan olabilr mi?
                //return rebus.Start().Advanced.Workers.Count;
                return rebus;
            });
        }

        public void QueueOneWay<TMessage>(string queueName, bool compress = false)
            where TMessage : class
        {
            queueName = _queueTopicNamePrefix + queueName;
            RegisteredMessageTypes.Add(typeof(TMessage));
            _services.AddTypedRebus<TMessage>(rebus =>
            {
                LogNewLine();
                Log.Information("Registering Queue {messagetype} one way.", typeof(TMessage).FullName);
                rebus
                    .Logging(x => x.Serilog(Log.Logger))
                    .Transport(x =>
                    {
                        x.UseAzureServiceBusQueueAsOneWayClient(_serviceBusConnectionString, _settings, compress)
                            .SetMessagePayloadSizeLimit(1024 * 1024) //1024 KB
                            ;
                    })
                    .Serialization(SerializerChoose())
                    .Options(CommonOptions)
                    .Routing(x => { x.TypeBased().Map<TMessage>(queueName); })
                    ;

                //ToDo: healthcheck burdan olabilr mi?
                //return rebus.Start().Advanced.Workers.Count;
                return rebus;
            });
        }

        public void QueueOneWayToMasstransit<TMessage>(string queueName) where TMessage : class
        {
            queueName = _queueTopicNamePrefix + queueName;
            RegisteredMessageTypes.Add(typeof(TMessage));
            _services.AddTypedRebus<TMessage>(rebus =>
            {
                LogNewLine();
                Log.Information("Registering Queue {messagetype} one way to {masstransit}.", typeof(TMessage).FullName, "masstransit");
                rebus
                    .Logging(x => x.Serilog(Log.Logger))
                    .Transport(x =>
                    {
                        x.UseAzureServiceBusQueueAsOneWayToMasstransit(_serviceBusConnectionString, _settings)
                            .SetMessagePayloadSizeLimit(1024 * 1024) //1024 KB
                            ;
                    })
                    .Serialization(s => s.UseNewtonsoftJson(GetNewtonsoftJsonSettings))
                    .Options(CommonOptions)
                    .Routing(x => { x.TypeBased().Map<TMessage>(queueName); })
                    ;

                //ToDo: healthcheck burdan olabilr mi?
                //return rebus.Start().Advanced.Workers.Count;
                return rebus;
            });
        }

        public void Topic<TMessage, TMessageHandler>(string topicName,
            string subscriberName,
            bool useMachineNameInSubscriberForMultipleNodes,
            bool compress = false)
            where TMessage : class
            where TMessageHandler : IHandleMessages, IHandleMessages<TMessage>
        {
            topicName = _queueTopicNamePrefix + topicName;
            if (useMachineNameInSubscriberForMultipleNodes)
            {
                var machineName = Environment.MachineName;
                machineName = machineName.Replace("/", "-").Replace(".", "-");
                subscriberName = $"{subscriberName}-{machineName}";
            }

            RegisteredMessageTypes.Add(typeof(TMessage));
            RebusHealthChecks.RegisteredQueueTopicItems.Add(new HealthCheckQueueTopicItem
            {
                TopicName = topicName,
                SubscriptionName = subscriberName,
                MessageType = typeof(TMessage)
            });
            _services.AddRebusHandler<TMessageHandler>();
            _services.AddRebusHandler<AzureDeferSecondLevelRetryHandler<TMessage>>();
            _services.AddTypedRebus<TMessage>(rebus =>
            {
                LogNewLine();
                Log.Information(
                    "Registering Topic {messagetype} with {subscriberName} subscription with handler of {handlertype}",
                    typeof(TMessage).FullName, subscriberName, typeof(TMessageHandler).FullName);
                rebus
                    .Logging(x => x.Serilog(Log.Logger))
                    .Transport(x =>
                    {
                        x.UseAzureServiceBusTopic(_serviceBusConnectionString, topicName,
                                subscriberName, _settings, compress)
                            .SetMessagePayloadSizeLimit(1024 * 1024) //1024 KB
                            .SetAutoDeleteOnIdle(TimeSpan.FromDays(7))
                            .SetMessagePeekLockDuration(TimeSpan.FromMinutes(5))
                            .SetDefaultMessageTimeToLive(TimeSpan.FromDays(1))
                            ;
                    })
                    .Serialization(SerializerChoose())
                    .Options(CommonOptions)
                    .Routing(x => x.TypeBased().Map<TMessage>(topicName))
                    ;

                //ToDo: healthcheck burdan olabilr mi?
                //return rebus.Start().Advanced.Workers.Count;
                return rebus;
            });
        }

        public void TopicOneWay<TMessage>(string topicName, bool compress = false)
            where TMessage : class
        {
            topicName = _queueTopicNamePrefix + topicName;
            RegisteredMessageTypes.Add(typeof(TMessage));
            _services.AddTypedRebus<TMessage>(rebus =>
            {
                LogNewLine();
                Log.Information("Registering Topic {messagetype} one way", typeof(TMessage).FullName);
                rebus
                    .Logging(x => x.Serilog(Log.Logger))
                    .Transport(x =>
                    {
                        x.UseAzureServiceBusTopicOneWay(_serviceBusConnectionString, topicName, _settings, compress)
                            .SetMessagePayloadSizeLimit(1024 * 1024) //1024 KB
                            .SetAutoDeleteOnIdle(TimeSpan.FromDays(7))
                            .SetMessagePeekLockDuration(TimeSpan.FromMinutes(5))
                            .SetDefaultMessageTimeToLive(TimeSpan.FromDays(1))
                            ;
                    })
                    .Serialization(SerializerChoose())
                    .Options(CommonOptions)
                    .Routing(x => x.TypeBased().Map<TMessage>(topicName))
                    ;

                //ToDo: healthcheck burdan olabilr mi?
                //return rebus.Start().Advanced.Workers.Count;
                return rebus;
            });
        }

        public void TopicOneWayToMasstransit<TMessage>(string topicName) where TMessage : class
        {
            topicName = _queueTopicNamePrefix + topicName;
            RegisteredMessageTypes.Add(typeof(TMessage));
            _services.AddTypedRebus<TMessage>(rebus =>
            {
                LogNewLine();
                Log.Information("Registering Topic {messagetype} one way to {masstransit}", typeof(TMessage).FullName, "masstransit");
                rebus
                    .Logging(x => x.Serilog(Log.Logger))
                    .Transport(x =>
                    {
                        x.UseAzureServiceBusTopicOneWayToMasstransit(_serviceBusConnectionString, topicName, _settings)
                            .SetMessagePayloadSizeLimit(1024 * 1024) //1024 KB
                            .SetAutoDeleteOnIdle(TimeSpan.FromDays(7))
                            .SetMessagePeekLockDuration(TimeSpan.FromMinutes(5))
                            .SetDefaultMessageTimeToLive(TimeSpan.FromDays(1))
                            ;
                    })
                    .Serialization(s => s.UseNewtonsoftJson(GetNewtonsoftJsonSettings))
                    .Options(CommonOptions)
                    .Routing(x => x.TypeBased().Map<TMessage>(topicName))
                    ;

                //ToDo: healthcheck burdan olabilr mi?
                //return rebus.Start().Advanced.Workers.Count;
                return rebus;
            });
        }


        public static void Configure(IApplicationBuilder app)
        {
            foreach (var messageType in RegisteredMessageTypes)
            {
                app.UseNamedRebus(messageType.Name);
            }
        }
    }
}