using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Exceptions;
using Rebus.Logging;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics
{
    internal class AzureTopicManager
    {
        readonly ServiceBusAdministrationClient _managementClient;
        readonly CancellationToken _cancellationToken;
        readonly ILog _log;
        private readonly AzureServiceBusTopicTransportSettings _settings;

        internal AzureTopicManager(ServiceBusAdministrationClient managementClient,
            CancellationToken cancellationToken,
            ILog log,
            AzureServiceBusTopicTransportSettings settings)
        {
            _managementClient = managementClient;
            _cancellationToken = cancellationToken;
            _log = log;
            _settings = settings;
        }

        internal CreateTopicOptions GetCreateTopicOptions(string normalizedTopicName)
        {
            var queueOptions = new CreateTopicOptions(normalizedTopicName);

            // if it's the input queue, do this:
            // must be set when the queue is first created
            queueOptions.EnablePartitioning = _settings.PartitioningEnabled;

            if (_settings.LockDuration.HasValue)
            {
                //TODO: lockduration yok create topicte?
                //queueOptions.LockDuration = LockDuration.Value;
            }

            if (_settings.DefaultMessageTimeToLive.HasValue)
            {
                queueOptions.DefaultMessageTimeToLive = _settings.DefaultMessageTimeToLive.Value;
            }

            if (_settings.DuplicateDetectionHistoryTimeWindow.HasValue)
            {
                queueOptions.RequiresDuplicateDetection = true;
                queueOptions.DuplicateDetectionHistoryTimeWindow = _settings.DuplicateDetectionHistoryTimeWindow.Value;
            }

            if (_settings.AutoDeleteOnIdle.HasValue)
            {
                queueOptions.AutoDeleteOnIdle = _settings.AutoDeleteOnIdle.Value;
            }

            // queueOptions.MaxDeliveryCount = 100;

            return queueOptions;
        }

        internal async Task<TopicProperties> EnsureTopicExists(string normalizedTopicName)
        {
            if (await _managementClient.TopicExistsAsync(normalizedTopicName, _cancellationToken).ConfigureAwait(false))
            {
                return await _managementClient.GetTopicAsync(normalizedTopicName, _cancellationToken).ConfigureAwait(false);
            }

            try
            {
                var createTopicOptions = GetCreateTopicOptions(normalizedTopicName);
                var result = await _managementClient.CreateTopicAsync(createTopicOptions, _cancellationToken).ConfigureAwait(false);
                _log.Debug($"Created topic {normalizedTopicName}");
                return result;
            }
            catch (ServiceBusException)
            {
                // most likely a race between two clients trying to create the same topic - we should be able to get it now
                return await _managementClient.GetTopicAsync(normalizedTopicName, _cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new ArgumentException($"Could not create topic '{normalizedTopicName}'", exception);
            }
        }

        internal void InnerCreateTopic(string normalizedAddress)
        {
            AsyncHelpers.RunSync(async () =>
            {
                //in ASB there cannot be queue&topics sharing same name: so delete any existing queue with that name
                var existingQueue = await _managementClient.QueueExistsAsync(normalizedAddress, _cancellationToken).ConfigureAwait(false);
                if (existingQueue)
                {
                    _log.Warn($"Deleting {normalizedAddress} queue in order to create a topic with that name");
                    await _managementClient.DeleteQueueAsync(normalizedAddress, _cancellationToken).ConfigureAwait(false);
                    _log.Warn($"Deleted {normalizedAddress} queue successfully, now creating topic");
                }

                try
                {
                    var topicExistsAsync = await _managementClient.TopicExistsAsync(normalizedAddress, _cancellationToken).ConfigureAwait(false);
                    if (topicExistsAsync)
                        return;
                    _log.Info("Creating ASB topic {queueName}", normalizedAddress);

                    var queueDescription = GetCreateTopicOptions(normalizedAddress);

                    await _managementClient.CreateTopicAsync(queueDescription, _cancellationToken).ConfigureAwait(false);
                }
                catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityAlreadyExists)
                {
                    // it's alright man it already exists
                }
                catch (Exception exception)
                {
                    throw new ArgumentException($"Could not create Azure Service Bus queue '{normalizedAddress}'", exception);
                }
            });
        }


        CreateSubscriptionOptions GetCreateSubscriptionOptions(string normalizedTopicName,
            string subscriptionName)
        {
            var options = new CreateSubscriptionOptions(normalizedTopicName, subscriptionName);

            //options.EnablePartitioning = PartitioningEnabled;

            if (_settings.LockDuration.HasValue)
            {
                options.LockDuration = _settings.LockDuration.Value;
            }
            
            if (_settings.DefaultMessageTimeToLive.HasValue)
            {
                options.DefaultMessageTimeToLive = _settings.DefaultMessageTimeToLive.Value;
            }

            if (_settings.DuplicateDetectionHistoryTimeWindow.HasValue)
            {
                _log.Info($"{normalizedTopicName}/{subscriptionName} cannot set DuplicateDetectionHistoryTimeWindow, unsupported for subscriptions.");
                // options.RequiresDuplicateDetection = true;
                // options.DuplicateDetectionHistoryTimeWindow = DuplicateDetectionHistoryTimeWindow.Value;
            }

            if (_settings.AutoDeleteOnIdle.HasValue)
            {
                options.AutoDeleteOnIdle = _settings.AutoDeleteOnIdle.Value;
            }

            options.MaxDeliveryCount = _settings.MaxDeliveryCount;
            return options;
        }

        internal async Task<SubscriptionProperties> GetOrCreateSubscription(string topicPath,
            string subscriptionName,
            AzureServiceBusTopicTransportSettings settings)
        {
            var createSubscriptionOptions = GetCreateSubscriptionOptions(topicPath, subscriptionName);
            if (await _managementClient.SubscriptionExistsAsync(topicPath, subscriptionName, _cancellationToken).ConfigureAwait(false))
            {
                return await _managementClient.GetSubscriptionAsync(topicPath, subscriptionName, _cancellationToken).ConfigureAwait(false);
            }

            try
            {
                _log.Info($"Creating {topicPath} topic / {subscriptionName} subscription");
                var result = await _managementClient.CreateSubscriptionAsync(createSubscriptionOptions, _cancellationToken).ConfigureAwait(false);
                _log.Info($"{topicPath} topic / {subscriptionName} subscription created successfully.");
                return result;
            }
            catch (ServiceBusException)
            {
                // most likely a race between two competing consumers - we should be able to get it now
                return await _managementClient.GetSubscriptionAsync(topicPath, subscriptionName, _cancellationToken).ConfigureAwait(false);
            }
        }

        internal void CheckInputTopicConfiguration(string address)
        {
            AsyncHelpers.RunSync(async () =>
            {
                var topicProperties = await GetTopicDescription(address).ConfigureAwait(false);

                if (topicProperties.EnablePartitioning != _settings.PartitioningEnabled)
                {
                    _log.Warn(
                        "The topic {queueName} has EnablePartitioning={enablePartitioning}, but the transport has PartitioningEnabled={partitioningEnabled}. As this setting cannot be changed after the queue is created, please either make sure the Rebus transport settings are consistent with the topic settings, or topic the queue and let Rebus create it again with the new settings.",
                        address, topicProperties.EnablePartitioning, _settings.PartitioningEnabled);
                }

                if (_settings.DuplicateDetectionHistoryTimeWindow.HasValue)
                {
                    var duplicateDetectionHistoryTimeWindow = _settings.DuplicateDetectionHistoryTimeWindow.Value;

                    if (!topicProperties.RequiresDuplicateDetection ||
                        topicProperties.DuplicateDetectionHistoryTimeWindow != duplicateDetectionHistoryTimeWindow)
                    {
                        _log.Warn(
                            "The topic {queueName} has RequiresDuplicateDetection={requiresDuplicateDetection}, but the transport has DuplicateDetectionHistoryTimeWindow={duplicateDetectionHistoryTimeWindow}. As this setting cannot be changed after the topic is created, please either make sure the Rebus transport settings are consistent with the topic settings, or delete the topic and let Rebus create it again with the new settings.",
                            address, topicProperties.RequiresDuplicateDetection, _settings.PartitioningEnabled);
                    }
                }
                else
                {
                    if (topicProperties.RequiresDuplicateDetection)
                    {
                        _log.Warn(
                            "The topic {queueName} has RequiresDuplicateDetection={requiresDuplicateDetection}, but the transport has DuplicateDetectionHistoryTimeWindow={duplicateDetectionHistoryTimeWindow}. As this setting cannot be changed after the topic is created, please either make sure the Rebus transport settings are consistent with the topic settings, or delete the topic and let Rebus create it again with the new settings.",
                            address, topicProperties.RequiresDuplicateDetection, _settings.PartitioningEnabled);
                    }
                }

                var updates = new List<string>();

                if (_settings.DefaultMessageTimeToLive.HasValue)
                {
                    var defaultMessageTimeToLive = _settings.DefaultMessageTimeToLive.Value;
                    if (topicProperties.DefaultMessageTimeToLive != defaultMessageTimeToLive)
                    {
                        topicProperties.DefaultMessageTimeToLive = defaultMessageTimeToLive;
                        updates.Add($"DefaultMessageTimeToLive = {defaultMessageTimeToLive}");
                    }
                }

                // if (LockDuration.HasValue)
                // {
                //     var lockDuration = LockDuration.Value;
                //     if (queueDescription.LockDuration != lockDuration)
                //     {
                //         queueDescription.LockDuration = lockDuration;
                //         updates.Add($"LockDuration = {lockDuration}");
                //     }
                // }

                if (_settings.AutoDeleteOnIdle.HasValue)
                {
                    var autoDeleteOnIdle = _settings.AutoDeleteOnIdle.Value;
                    if (topicProperties.AutoDeleteOnIdle != autoDeleteOnIdle)
                    {
                        topicProperties.AutoDeleteOnIdle = autoDeleteOnIdle;
                        updates.Add($"AutoDeleteOnIdle = {autoDeleteOnIdle}");
                    }
                }

                if (!updates.Any()) return;

                _log.Info("Updating ASB topic {queueName}: {updates}", address, updates);
                await _managementClient.UpdateTopicAsync(topicProperties, _cancellationToken);
            });
        }
        
        internal async Task<TopicProperties> GetTopicDescription(string address)
        {
            try
            {
                //GetOrCreate topic first
                InnerCreateTopic(address);
                var result = await _managementClient.GetTopicAsync(address, _cancellationToken).ConfigureAwait(false);
                return result.Value;
            }
            catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
            {
                throw new RebusApplicationException(exception, $"Could not get topic description for queue {address}");
            }
            catch (Exception exception)
            {
                throw new RebusApplicationException(exception, $"Could not get topic description for queue {address}");
            }
        }


    }
}