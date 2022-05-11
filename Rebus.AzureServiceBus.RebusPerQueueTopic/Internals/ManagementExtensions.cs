using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Internals
{
    public static class ManagementExtensions
    {
        public static async Task DeleteQueueIfExistsAsync(this ServiceBusAdministrationClient client,
            string queuePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                await client.DeleteQueueAsync(queuePath, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceBusException)
            {
                // it's ok man
            }
        }

        public static async Task CreateQueueIfNotExistsAsync(this ServiceBusAdministrationClient client,
            string queuePath,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                await client.CreateQueueAsync(queuePath, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceBusException)
            {
                // it's ok man
            }
        }

        public static async Task PurgeQueue(string connectionString,
            string queueName,
            CancellationToken cancellationToken = default)
        {
            await using var serviceBusClient = new ServiceBusClient(connectionString);
            await using var messageReceiver = serviceBusClient.CreateReceiver(queueName, new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.ReceiveAndDelete
            });

            try
            {
                while (true)
                {
                    var messages = await messageReceiver.ReceiveMessagesAsync(100, TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);

                    if (messages == null) break;
                    if (!messages.Any()) break;
                }
            }
            catch (ServiceBusException)
            {
                // ignore it then
            }
            finally
            {
                await messageReceiver.CloseAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        public static async Task RemoveTopic(string connectionString,
            string topicName,
            CancellationToken cancellationToken = default)
        {
            await using var client = new ServiceBusClient(connectionString);
            var managementClient = new ServiceBusAdministrationClient(connectionString);
            await managementClient.DeleteTopicAsync(topicName, cancellationToken);
        }

        public static async Task RemoveTopicSubscription(string connectionString,
            string topicName,
            string subscriptionName,
            CancellationToken cancellationToken = default)
        {
            await using var client = new ServiceBusClient(connectionString);
            var managementClient = new ServiceBusAdministrationClient(connectionString);
            await managementClient.DeleteSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        }

        public static async Task RemoveAllSubscriptions(string connectionString,
            string topicName,
            CancellationToken cancellationToken = default)
        {
            await using var client = new ServiceBusClient(connectionString);
            var managementClient = new ServiceBusAdministrationClient(connectionString);
            await foreach (var subscription in managementClient.GetSubscriptionsAsync(topicName))
            {
                await managementClient.DeleteSubscriptionAsync(topicName, subscription.SubscriptionName, cancellationToken);
            }
        }
    }
}