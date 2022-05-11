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

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue
{
    internal class AzureQueueManager
    {
        readonly ServiceBusAdministrationClient _managementClient;
        readonly CancellationToken _cancellationToken;
        readonly ILog _log;
        private readonly AzureServiceBusQueueTransportSettings _settings;

        internal AzureQueueManager(ServiceBusAdministrationClient managementClient,
            CancellationToken cancellationToken,
            ILog log,
            AzureServiceBusQueueTransportSettings settings)
        {
            _managementClient = managementClient;
            _cancellationToken = cancellationToken;
            _log = log;
            _settings = settings;
        }


        public CreateQueueOptions GetInputQueueDescription(string normalizedAddress,
            string address)
        {
            var queueOptions = new CreateQueueOptions(normalizedAddress);

            // if it's the input queue, do this:
            if (normalizedAddress == address)
            {
                // must be set when the queue is first created
                queueOptions.EnablePartitioning = _settings.PartitioningEnabled;

                if (_settings.LockDuration.HasValue)
                {
                    queueOptions.LockDuration = _settings.LockDuration.Value;
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

                queueOptions.MaxDeliveryCount = _settings.MaxDeliveryCount;
            }

            return queueOptions;
        }

        public void InnerCreateQueue(string normalizedAddress,
            string address)
        {
            // one-way client does not create any queues
            if (address == null)
            {
                return;
            }

            if (_settings.DoNotCreateQueuesEnabled)
            {
                _log.Info("Transport configured to not create queue - skipping existence check and potential creation for {queueName}",
                    normalizedAddress);
                return;
            }

            AsyncHelpers.RunSync(async () =>
            {
                if (await _managementClient.QueueExistsAsync(normalizedAddress, _cancellationToken).ConfigureAwait(false)) return;

                try
                {
                    _log.Info("Creating ASB queue {queueName}", normalizedAddress);

                    var queueDescription = GetInputQueueDescription(normalizedAddress, address);

                    await _managementClient.CreateQueueAsync(queueDescription, _cancellationToken).ConfigureAwait(false);
                }
                catch (ServiceBusException)
                {
                    // it's alright man
                }
                catch (Exception exception)
                {
                    throw new ArgumentException($"Could not create Azure Service Bus queue '{normalizedAddress}'", exception);
                }
            });
        }

        public async Task<QueueProperties> GetQueueDescription(string address)
        {
            try
            {
                return await _managementClient.GetQueueAsync(address, _cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new RebusApplicationException(exception, $"Could not get queue description for queue {address}");
            }
        }

  
        public void CheckInputQueueConfiguration(string address)
        {
            if (_settings.DoNotCheckQueueConfigurationEnabled)
            {
                _log.Info("Transport configured to not check queue configuration - skipping existence check for {queueName}", address);
                return;
            }

            AsyncHelpers.RunSync(async () =>
            {
                var queueDescription = await this.GetQueueDescription(address).ConfigureAwait(false);

                if (queueDescription.EnablePartitioning != _settings.PartitioningEnabled)
                {
                    _log.Warn(
                        "The queue {queueName} has EnablePartitioning={enablePartitioning}, but the transport has PartitioningEnabled={partitioningEnabled}. As this setting cannot be changed after the queue is created, please either make sure the Rebus transport settings are consistent with the queue settings, or delete the queue and let Rebus create it again with the new settings.",
                        address, queueDescription.EnablePartitioning, _settings.PartitioningEnabled);
                }

                if (_settings.DuplicateDetectionHistoryTimeWindow.HasValue)
                {
                    var duplicateDetectionHistoryTimeWindow = _settings.DuplicateDetectionHistoryTimeWindow.Value;

                    if (!queueDescription.RequiresDuplicateDetection ||
                        queueDescription.DuplicateDetectionHistoryTimeWindow != duplicateDetectionHistoryTimeWindow)
                    {
                        _log.Warn(
                            "The queue {queueName} has RequiresDuplicateDetection={requiresDuplicateDetection}, but the transport has DuplicateDetectionHistoryTimeWindow={duplicateDetectionHistoryTimeWindow}. As this setting cannot be changed after the queue is created, please either make sure the Rebus transport settings are consistent with the queue settings, or delete the queue and let Rebus create it again with the new settings.",
                            address, queueDescription.RequiresDuplicateDetection, _settings.PartitioningEnabled);
                    }
                }
                else
                {
                    if (queueDescription.RequiresDuplicateDetection)
                    {
                        _log.Warn(
                            "The queue {queueName} has RequiresDuplicateDetection={requiresDuplicateDetection}, but the transport has DuplicateDetectionHistoryTimeWindow={duplicateDetectionHistoryTimeWindow}. As this setting cannot be changed after the queue is created, please either make sure the Rebus transport settings are consistent with the queue settings, or delete the queue and let Rebus create it again with the new settings.",
                            address, queueDescription.RequiresDuplicateDetection, _settings.PartitioningEnabled);
                    }
                }

                var updates = new List<string>();

                if (_settings.DefaultMessageTimeToLive.HasValue)
                {
                    var defaultMessageTimeToLive = _settings.DefaultMessageTimeToLive.Value;
                    if (queueDescription.DefaultMessageTimeToLive != defaultMessageTimeToLive)
                    {
                        queueDescription.DefaultMessageTimeToLive = defaultMessageTimeToLive;
                        updates.Add($"DefaultMessageTimeToLive = {defaultMessageTimeToLive}");
                    }
                }

                if (_settings.LockDuration.HasValue)
                {
                    var lockDuration = _settings.LockDuration.Value;
                    if (queueDescription.LockDuration != lockDuration)
                    {
                        queueDescription.LockDuration = lockDuration;
                        updates.Add($"LockDuration = {lockDuration}");
                    }
                }

                if (_settings.AutoDeleteOnIdle.HasValue)
                {
                    var autoDeleteOnIdle = _settings.AutoDeleteOnIdle.Value;
                    if (queueDescription.AutoDeleteOnIdle != autoDeleteOnIdle)
                    {
                        queueDescription.AutoDeleteOnIdle = autoDeleteOnIdle;
                        updates.Add($"AutoDeleteOnIdle = {autoDeleteOnIdle}");
                    }
                }

                if (!updates.Any()) return;

                if (_settings.DoNotCreateQueuesEnabled)
                {
                    _log.Warn(
                        "Detected changes in the settings for the queue {queueName}: {updates} - but the transport is configured to NOT create queues, so no settings will be changed",
                        address, updates);
                    return;
                }

                _log.Info("Updating ASB queue {queueName}: {updates}", address, updates);
                await _managementClient.UpdateQueueAsync(queueDescription, _cancellationToken);
            });
        }
    }
}