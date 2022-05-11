using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Serilog;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.HealthChecks
{
    /// <summary>
    /// Masstransit IBus'un healthcheck'ini zorla tetiklemek için mevcut.
    /// Bağlantı problemlerinde kendi healthcheck'i ile onu yakayalamıyoruz.
    /// </summary>
    public class RebusHealthChecks
    {
        private readonly string _connectionString;
        private readonly BusHealthCheckOptions _options;
        internal static DateTime AppStartupTime { get; set; }
        internal static readonly ConcurrentBag<HealthCheckQueueTopicItem> RegisteredQueueTopicItems = new();

        private string Name => nameof(RebusHealthChecks);

        public RebusHealthChecks(string connectionString,
            BusHealthCheckOptions options)
        {
            _connectionString = connectionString;
            _options = options;
        }

        public async Task<HealthCheckResult> CheckHealthAsync(CancellationToken cancellationToken = default)
        {
            var startupDiff = DateTime.Now.Subtract(AppStartupTime);
            if (startupDiff < _options.WarmupIgnoreDelayForHealthChecking)
            {
                return HealthCheckResult.Healthy("Health checking ignored, still in warmup phase due configuration",
                    new Dictionary<string, object>
                    {
                        {"AppStartupDelayForHealthChecking", _options.WarmupIgnoreDelayForHealthChecking.ToString()},
                        {"ElapsedDifference", startupDiff.ToString()}
                    });
            }

            var clientOptions = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets
            };
            var serviceBusReceiverOptions = new ServiceBusReceiverOptions
            {
                PrefetchCount = 0,
                ReceiveMode = ServiceBusReceiveMode.PeekLock
            };


            var result = HealthStatus.Healthy;
            var data = new Dictionary<string, object>();
            await using (var client = new ServiceBusClient(_connectionString, clientOptions))
                foreach (var item in RegisteredQueueTopicItems)
                {
                    if (!string.IsNullOrEmpty(item.QueueName))
                    {
                        var health = await CheckQueueHealth(cancellationToken, item, client, serviceBusReceiverOptions);
                        data.Add(item.MessageType.ToString(), health);
                        if (health.Status == HealthStatus.Unhealthy)
                            result = HealthStatus.Unhealthy;
                    }
                    else if (!string.IsNullOrEmpty(item.TopicName) && !string.IsNullOrEmpty(item.SubscriptionName))
                    {
                        var health = await CheckTopicHealth(cancellationToken, item, client, serviceBusReceiverOptions);
                        data.Add(item.MessageType.ToString(), health);
                        if (health.Status == HealthStatus.Unhealthy)
                            result = HealthStatus.Unhealthy;
                    }
                    else
                    {
                        Log.Error("[{Name}] cannot determine health check for item. {item}", Name, item);
                    }
                }

            return new HealthCheckResult(result, data: data);
        }

        private async Task<HealthCheckResult> CheckTopicHealth(CancellationToken cancellationToken,
            HealthCheckQueueTopicItem item,
            ServiceBusClient client,
            ServiceBusReceiverOptions serviceBusReceiverOptions
        )
        {
            var now = DateTimeOffset.Now;
            Log.Debug("[{Name}] Checking topic health of '{busType}'", Name, item.TopicName);
            await using (var messageReceiver = client.CreateReceiver(item.TopicName, item.SubscriptionName, serviceBusReceiverOptions))
            {
                var message = await messageReceiver.PeekMessageAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (message == null)
                {
                    var msg = $"[{Name}] There is no message in {item}, OK";
                    return HealthCheckResult.Healthy(msg,
                        data: new Dictionary<string, object>
                        {
                            ["Item"] = item.ToString(),
                            ["ExpireConditionDelay"] = null,
                            ["MessageEnqueueDate"] = null,
                            ["Diff"] = null
                        });
                }

                var expireConditionDelay = _options.ExpireConditionDelay(message, item.MessageType);
                var enqueueDiff = now.Subtract(message.EnqueuedTime);
                if (enqueueDiff <= expireConditionDelay)
                {
                    var msg =
                        $"[{Name}] {item} health check OK; ExpireDelay:{expireConditionDelay} MessageDate: {message.EnqueuedTime}, Diff:{enqueueDiff}";
                    return HealthCheckResult.Healthy(msg,
                        data: new Dictionary<string, object>
                        {
                            ["Item"] = item.ToString(),
                            ["ExpireConditionDelay"] = expireConditionDelay.ToString(),
                            ["MessageEnqueueDate"] = message.EnqueuedTime,
                            ["Diff"] = enqueueDiff.ToString()
                        });
                }

                return HealthCheckResult.Unhealthy($"{item} message is too long at the queue, resulting in Unhealthy healthcheck",
                    data: new Dictionary<string, object>
                    {
                        ["Item"] = item.ToString(),
                        ["ExpireConditionDelay"] = expireConditionDelay.ToString(),
                        ["MessageEnqueueDate"] = message.EnqueuedTime,
                        ["Diff"] = enqueueDiff.ToString()
                    });
            }
        }

        private async Task<HealthCheckResult> CheckQueueHealth(CancellationToken cancellationToken,
            HealthCheckQueueTopicItem item,
            ServiceBusClient client,
            ServiceBusReceiverOptions serviceBusReceiverOptions
        )
        {
            var now = DateTimeOffset.Now;
            Log.Debug("[{Name}] Checking queue health of {item}", Name, item);
            await using (var messageReceiver = client.CreateReceiver(item.QueueName, serviceBusReceiverOptions))
            {
                var message = await messageReceiver.PeekMessageAsync(cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                if (message == null)
                {
                    var msg = $"[{Name}] There is no message in {item}, OK";
                    return HealthCheckResult.Healthy(msg,
                        data: new Dictionary<string, object>
                        {
                            ["Item"] = item.ToString(),
                            ["ExpireConditionDelay"] = null,
                            ["MessageEnqueueDate"] = null,
                            ["Diff"] = null
                        });
                }

                var expireConditionDelay = _options.ExpireConditionDelay(message, item.MessageType);
                var enqueueDiff = now.Subtract(message.EnqueuedTime);
                if (enqueueDiff <= expireConditionDelay)
                {
                    var msg =
                        $"[{Name}] {item} health check OK; ExpireDelay:{expireConditionDelay} MessageDate: {message.EnqueuedTime}, Diff:{enqueueDiff}";
                    return HealthCheckResult.Healthy(msg,
                        data: new Dictionary<string, object>
                        {
                            ["Item"] = item.ToString(),
                            ["ExpireConditionDelay"] = expireConditionDelay.ToString(),
                            ["MessageEnqueueDate"] = message.EnqueuedTime,
                            ["Diff"] = enqueueDiff.ToString()
                        });
                }

                return HealthCheckResult.Unhealthy($"{item.QueueName} message is too long at the queue, resulting in Unhealthy healthcheck",
                    data: new Dictionary<string, object>
                    {
                        ["Item"] = item.ToString(),
                        ["ExpireConditionDelay"] = expireConditionDelay.ToString(),
                        ["MessageEnqueueDate"] = message.EnqueuedTime,
                        ["Diff"] = enqueueDiff.ToString()
                    });
            }
        }
    }
}