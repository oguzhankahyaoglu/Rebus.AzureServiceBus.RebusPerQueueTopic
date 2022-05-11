using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus.NameFormat;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Bus;
using Rebus.Exceptions;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Subscriptions;
using Rebus.Threading;
using Rebus.Transport;

// ReSharper disable RedundantArgumentDefaultValue
// ReSharper disable ArgumentsStyleNamedExpression
// ReSharper disable ArgumentsStyleOther
// ReSharper disable ArgumentsStyleLiteral
// ReSharper disable ArgumentsStyleAnonymousFunction
#pragma warning disable 1998

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigMasstransit
{
    /// <summary>
    /// Implementation of <see cref="ITransport"/> that uses Azure Service Bus queues to send/receive messages.
    /// </summary>
    public class AzureServiceBusQueueOneWayToMasstransitTransport : ITransport, IInitializable, IDisposable,
        ISubscriptionStorage
    {
        /// <summary>
        /// Outgoing messages are stashed in a concurrent queue under this key
        /// </summary>
        const string OutgoingMessagesKey = "new-azure-service-bus-transport";

        /// <summary>
        /// Subscriber "addresses" are prefixed with this bad boy so we can recognize them and publish to a topic client instead
        /// </summary>
        const string MagicSubscriptionPrefix = "***Topic***: ";

        /// <summary>
        /// External timeout manager address set to this magic address will be routed to the destination address specified by the <see cref="Headers.DeferredRecipient"/> header
        /// </summary>
        public const string MagicDeferredMessagesAddress = "___deferred___";

        readonly ConcurrentStack<IDisposable> _disposables = new();
        readonly ConcurrentDictionary<string, string[]> _cachedSubscriberAddresses = new();
        readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _messageSenders = new();
        readonly CancellationToken _cancellationToken;
        readonly INameFormatter _nameFormatter;
        readonly ILog _log;
        readonly ServiceBusClient _client;

        /// <summary>
        /// Constructs the transport, connecting to the service bus pointed to by the connection string.
        /// </summary>
        public AzureServiceBusQueueOneWayToMasstransitTransport(string connectionString,
            IRebusLoggerFactory rebusLoggerFactory,
            IAsyncTaskFactory asyncTaskFactory,
            INameFormatter nameFormatter,
            CancellationToken cancellationToken = default,
            TokenCredential tokenCredential = null)
        {
            if (rebusLoggerFactory == null) throw new ArgumentNullException(nameof(rebusLoggerFactory));
            if (asyncTaskFactory == null) throw new ArgumentNullException(nameof(asyncTaskFactory));
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            _nameFormatter = nameFormatter;

            _cancellationToken = cancellationToken;
            _log = rebusLoggerFactory.GetLogger<AzureServiceBusQueueTransport>();

            var connectionStringParser = new ConnectionStringParser(connectionString);

            var clientOptions = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets,
                RetryOptions = AzureRebusCommon.DefaultRetryStrategy(),
            };

            if (tokenCredential != null)
            {
                var connectionStringProperties = ServiceBusConnectionStringProperties.Parse(connectionString);
                // new ServiceBusAdministrationClient(connectionStringProperties.FullyQualifiedNamespace, tokenCredential);
                _client = new ServiceBusClient(connectionStringProperties.FullyQualifiedNamespace, tokenCredential,
                    clientOptions);
            }
            else
            {
                // detect Authentication=Managed Identity
                //if (_connectionStringParser.Contains("Authentication", "Managed Identity", StringComparison.OrdinalIgnoreCase))
                //{
                //    var connectionStringProperties = ServiceBusConnectionStringProperties.Parse(connectionString);

                //    _tokenCredential = new ManagedIdentityCredential();
                //    _client = new ServiceBusClient(connectionStringProperties.FullyQualifiedNamespace, _tokenCredential, clientOptions);
                //    _managementClient = new ServiceBusAdministrationClient(connectionStringProperties.FullyQualifiedNamespace, _tokenCredential);
                //}
                //else
                //{
                var connectionStringWithoutEntityPath = connectionStringParser.GetConnectionStringWithoutEntityPath();

                _client = new ServiceBusClient(connectionStringWithoutEntityPath, clientOptions);
                // new ServiceBusAdministrationClient(connectionStringWithoutEntityPath);
                //}
            }

        }

        /// <summary>
        /// Gets "subscriber addresses" by getting one single magic "queue name", which is then
        /// interpreted as a publish operation to a topic when the time comes to send to that "queue"
        /// </summary>
        public async Task<string[]> GetSubscriberAddresses(string topic)
        {
            return _cachedSubscriberAddresses.GetOrAdd(topic, _ => new[] { $"{MagicSubscriptionPrefix}{topic}" });
        }

        /// <summary>
        /// Registers this endpoint as a subscriber by creating a subscription for the given topic, setting up
        /// auto-forwarding from that subscription to this endpoint's input queue
        /// </summary>
        public async Task RegisterSubscriber(string topic,
            string subscriberAddress)
        {
            throw new NotImplementedException($"{GetType().Name} will not be able to use Topics.");
        }

        /// <summary>
        /// Unregisters this endpoint as a subscriber by deleting the subscription for the given topic
        /// </summary>
        public async Task UnregisterSubscriber(string topic,
            string subscriberAddress)
        {
            throw new NotImplementedException($"{GetType().Name} will not be able to use Topics.");
        }

        /// <summary>
        /// Creates a queue with the given address
        /// </summary>
        public void CreateQueue(string address)
        {
            address = _nameFormatter.FormatQueueName(address);

            InnerCreateQueue(address);
        }

        void InnerCreateQueue(string normalizedAddress)
        {
            // one-way client does not create any queues
        }

        /// <inheritdoc />
        /// <summary>
        /// Sends the given message to the queue with the given <paramref name="destinationAddress" />
        /// </summary>
        public async Task Send(string destinationAddress,
            TransportMessage message,
            ITransactionContext context)
        {
            var actualDestinationAddress = GetActualDestinationAddress(destinationAddress, message);
            var outgoingMessages = GetOutgoingMessages(context);
            outgoingMessages.Enqueue(new OutgoingMessage(actualDestinationAddress, message));
        }

        string GetActualDestinationAddress(string destinationAddress,
            TransportMessage message)
        {
            if (message.Headers.ContainsKey(Headers.DeferredUntil))
            {
                if (destinationAddress == MagicDeferredMessagesAddress)
                {
                    try
                    {
                        return message.Headers.GetValue(Headers.DeferredRecipient);
                    }
                    catch (Exception exception)
                    {
                        throw new ArgumentException(
                            $"The destination address was set to '{MagicDeferredMessagesAddress}', but no '{Headers.DeferredRecipient}' header could be found on the message",
                            exception);
                    }
                }

                if (message.Headers.TryGetValue(Headers.DeferredRecipient, out var deferredRecipient))
                {
                    return _nameFormatter.FormatQueueName(deferredRecipient);
                }
            }

            if (!destinationAddress.StartsWith(MagicSubscriptionPrefix))
            {
                return _nameFormatter.FormatQueueName(destinationAddress);
            }

            return destinationAddress;
        }

        private ServiceBusMessage GetMessage(OutgoingMessage outgoingMessage)
        {
            var transportMessage = outgoingMessage.TransportMessage;
            var message = new ServiceBusMessage(transportMessage.Body);
            var headers = transportMessage.Headers.Clone();

            if (headers.TryGetValue(Headers.TimeToBeReceived, out var timeToBeReceivedStr))
            {
                var timeToBeReceived = TimeSpan.Parse(timeToBeReceivedStr);
                message.TimeToLive = timeToBeReceived;
                headers.Remove(Headers.TimeToBeReceived);
            }

            if (headers.TryGetValue(Headers.DeferredUntil, out var deferUntilTime))
            {
                var deferUntilDateTimeOffset = deferUntilTime.ToDateTimeOffset();
                message.ScheduledEnqueueTime = deferUntilDateTimeOffset;
                headers.Remove(Headers.DeferredUntil);
            }

            if (headers.TryGetValue(Headers.CorrelationId, out var correlationId))
            {
                message.CorrelationId = correlationId;
            }

            if (headers.TryGetValue(Headers.MessageId, out var messageId))
            {
                message.MessageId = messageId;
            }
            else
            {
                var (parsedMessageId, parsedType) =
                    MasstransitHelpers.SetMessageIdFromMasstransitMessage(message.Body.ToArray(), _log);
                message.MessageId = parsedMessageId;
            }

            message.Subject = transportMessage.GetMessageLabel();

            if (headers.TryGetValue(Headers.ErrorDetails, out var errorDetails))
            {
                // this particular header has a tendency to grow out of hand
                headers[Headers.ErrorDetails] = errorDetails.TrimTo(32000);
            }

            foreach (var kvp in headers)
            {
                message.ApplicationProperties[kvp.Key] = kvp.Value;
            }

            return message;
        }

        ConcurrentQueue<OutgoingMessage> GetOutgoingMessages(ITransactionContext context)
        {
            ConcurrentQueue<OutgoingMessage> CreateNewOutgoingMessagesQueue()
            {
                var messagesToSend = new ConcurrentQueue<OutgoingMessage>();

                async Task SendOutgoingMessages(ITransactionContext ctx)
                {
                    var messagesByDestinationQueue = messagesToSend.GroupBy(m => m.DestinationAddress);

                    async Task SendOutgoingMessagesToDestination(IGrouping<string, OutgoingMessage> group)
                    {
                        var destinationQueue = group.Key;
                        var messages = group;

                        if (destinationQueue.StartsWith(MagicSubscriptionPrefix))
                        {
                            throw new NotImplementedException($"{GetType().Name} will not be able to use Topics.");
                        }

                        var messageSender = GetMessageSender(destinationQueue);
                        var serviceBusMessageBatches = await GetBatches(messages.Select(GetMessage), messageSender);

                        using (serviceBusMessageBatches.AsDisposable(b => b.DisposeCollection()))
                        {
                            foreach (var batch in serviceBusMessageBatches)
                            {
                                try
                                {
                                    await messageSender
                                        .SendMessagesAsync(batch, _cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (Exception exception)
                                {
                                    throw new RebusApplicationException(exception,
                                        $"Could not send to queue '{destinationQueue}'");
                                }
                            }
                        }
                    }

                    await Task.WhenAll(messagesByDestinationQueue
                            .Select(SendOutgoingMessagesToDestination))
                        .ConfigureAwait(false);
                }

                context.OnCommitted(SendOutgoingMessages);

                return messagesToSend;
            }

            return context.GetOrAdd(OutgoingMessagesKey, CreateNewOutgoingMessagesQueue);
        }

        async Task<IReadOnlyList<ServiceBusMessageBatch>> GetBatches(IEnumerable<ServiceBusMessage> messages,
            ServiceBusSender sender)
        {
            async ValueTask<ServiceBusMessageBatch> CreateMessageBatchAsync()
            {
                try
                {
                    return await sender.CreateMessageBatchAsync(_cancellationToken);
                }
                catch (ServiceBusException exception) when (exception.Reason ==
                                                            ServiceBusFailureReason.MessagingEntityNotFound)
                {
                    throw new RebusApplicationException(exception,
                        $"Message batch creation failed, because the messaging entity with path '{sender.EntityPath}' does not exist");
                }
            }

            var batches = new List<ServiceBusMessageBatch>();
            var currentBatch = await CreateMessageBatchAsync();

            foreach (var message in messages)
            {
                if (currentBatch.TryAddMessage(message)) continue;

                batches.Add(currentBatch);
                currentBatch = await CreateMessageBatchAsync();

                if (currentBatch.TryAddMessage(message)) continue;

                throw new ArgumentException(
                    $"The message {message} could not be added to a brand new message batch (batch max size is {currentBatch.MaxSizeInBytes} bytes)");
            }

            if (currentBatch.Count > 0)
            {
                batches.Add(currentBatch);
            }

            return batches;
        }

        /// <summary>
        /// Receives the next message from the input queue. Returns null if no message was available
        /// </summary>
        public async Task<TransportMessage> Receive(ITransactionContext context,
            CancellationToken cancellationToken)
        {
            return null;
        }

        /// <summary>
        /// Gets the input queue name for this transport
        /// </summary>
        public string Address => null;

        /// <summary>
        /// Initializes the transport by ensuring that the input queue has been created
        /// </summary>
        /// <inheritdoc />
        public void Initialize()
        {
            _log.Info("Initializing one-way Azure Service Bus transport");
        }

        /// <summary>
        /// Always returns true because Azure Service Bus topics and subscriptions are global
        /// </summary>
        public bool IsCentralized => true;

        /// <summary>
        /// Configures the maximum total message payload in bytes when auto-batching outgoing messages. Should probably only be modified if the SKU allows for greater payload sizes
        /// (e.g. 'Premium' at the time of writing allows for 1 MB) Please add some leeway, because Rebus' payload size estimation is not entirely precise
        /// </summary>
        public int MaximumMessagePayloadBytes { get; set; }


        /// <summary>
        /// Disposes all resources associated with this particular transport instance
        /// </summary>
        public void Dispose()
        {
            var disposables = new List<IDisposable>();

            while (_disposables.TryPop(out var disposable))
            {
                disposables.Add(disposable);
            }

            Parallel.ForEach(disposables, d => d.Dispose());
        }

        ServiceBusSender GetMessageSender(string queue) => _messageSenders.GetOrAdd(queue, _ => new(() =>
        {
            var messageSender = _client.CreateSender(queue);

            _disposables.Push(messageSender.AsDisposable(t => AsyncHelpers.RunSync(async () =>
            {
                try
                {
                    await t.CloseAsync(_cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    // we're being cancelled
                }
            })));

            return messageSender;
        })).Value;
    }
}