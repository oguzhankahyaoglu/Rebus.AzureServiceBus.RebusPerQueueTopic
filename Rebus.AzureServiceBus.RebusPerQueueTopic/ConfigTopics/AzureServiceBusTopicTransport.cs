﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus.NameFormat;
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

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics
{
    /// <summary>
    /// Implementation of <see cref="ITransport"/> that uses Azure Service Bus topics to send/receive messages.
    /// </summary>
    public class AzureServiceBusTopicTransport : ITransport, IInitializable, IDisposable, ISubscriptionStorage
    {
        /// <summary>
        /// Outgoing messages are stashed in a concurrent queue under this key
        /// </summary>
        const string OutgoingMessagesKey = "new-azure-service-bus-transport";

        /// <summary>
        /// External timeout manager address set to this magic address will be routed to the destination address specified by the <see cref="Headers.DeferredRecipient"/> header
        /// </summary>
        public const string MagicDeferredMessagesAddress = "___deferred___";

        readonly ExceptionIgnorant _subscriptionExceptionIgnorant =
            new ExceptionIgnorant(maxAttemps: 10).Ignore<ServiceBusException>(ex => ex.IsTransient);

        readonly ConcurrentStack<IDisposable> _disposables = new();
        // readonly ConcurrentDictionary<string, MessageLockRenewer> _messageLockRenewers = new();
        readonly ConcurrentDictionary<string, string[]> _cachedSubscriberAddresses = new();
        readonly ConcurrentDictionary<string, Lazy<ServiceBusSender>> _messageSenders = new();
        readonly ConcurrentDictionary<string, Lazy<Task<ServiceBusSender>>> _topicClients = new();
        readonly CancellationToken _cancellationToken;
        // readonly IAsyncTask _messageLockRenewalTask;
        readonly ServiceBusAdministrationClient _managementClient;
        readonly ConnectionStringParser _connectionStringParser;
        readonly TokenCredential _tokenCredential;
        readonly INameFormatter _nameFormatter;
        internal readonly string SubscriptionName;
        readonly ILog _log;
        readonly ServiceBusClient _client;

        bool _prefetchingEnabled;
        int _prefetchCount;

        ServiceBusReceiver _messageReceiver;
        private readonly AzureTopicManager _azureTopicManager;
        private readonly AzureServiceBusTopicTransportSettings _settings;

        /// <summary>
        /// Constructs the transport, connecting to the service bus pointed to by the connection string.
        /// </summary>
        public AzureServiceBusTopicTransport(string connectionString,
            string topicName,
            string subscriptionName,
            IRebusLoggerFactory rebusLoggerFactory,
            IAsyncTaskFactory asyncTaskFactory,
            INameFormatter nameFormatter,
            AzureServiceBusTopicTransportSettings settings,
            CancellationToken cancellationToken = default,
            TokenCredential tokenCredential = null)
        {
            if (rebusLoggerFactory == null) throw new ArgumentNullException(nameof(rebusLoggerFactory));
            if (asyncTaskFactory == null) throw new ArgumentNullException(nameof(asyncTaskFactory));
            if (connectionString == null) throw new ArgumentNullException(nameof(connectionString));

            _nameFormatter = nameFormatter;
            _settings = settings;

            if (topicName == null)
            {
                throw new ArgumentException($"topicName parameter is required in order to use ASB Topics: '{topicName}'");
            }

            Address = _nameFormatter.FormatTopicName(topicName);
            SubscriptionName = _nameFormatter.FormatSubscriptionName(subscriptionName);

            _cancellationToken = cancellationToken;
            _log = rebusLoggerFactory.GetLogger<AzureServiceBusTopicTransport>();

            _connectionStringParser = new ConnectionStringParser(connectionString);

            var clientOptions = new ServiceBusClientOptions
            {
                TransportType = ServiceBusTransportType.AmqpWebSockets,
                RetryOptions = AzureRebusCommon.DefaultRetryStrategy(),
            };

            if (tokenCredential != null)
            {
                var connectionStringProperties = ServiceBusConnectionStringProperties.Parse(connectionString);
                _managementClient = new ServiceBusAdministrationClient(connectionStringProperties.FullyQualifiedNamespace, tokenCredential);
                _client = new ServiceBusClient(connectionStringProperties.FullyQualifiedNamespace, tokenCredential, clientOptions);
                _tokenCredential = tokenCredential;
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
                var connectionStringWithoutEntityPath = _connectionStringParser.GetConnectionStringWithoutEntityPath();

                _client = new ServiceBusClient(connectionStringWithoutEntityPath, clientOptions);
                _managementClient = new ServiceBusAdministrationClient(connectionStringWithoutEntityPath);
                //}
            }

            // _messageLockRenewalTask = asyncTaskFactory.Create("Peek Lock Renewal", RenewPeekLocks, prettyInsignificant: true, intervalSeconds: 10);
            _azureTopicManager = new AzureTopicManager(_managementClient, _cancellationToken, _log, settings);
        }

        /// <summary>
        /// Gets "subscriber addresses" by getting one single magic "queue name", which is then
        /// interpreted as a publish operation to a topic when the time comes to send to that "queue"
        /// </summary>
        public async Task<string[]> GetSubscriberAddresses(string topic)
        {
            var result = _cachedSubscriberAddresses.GetOrAdd(topic, _ => new[] {$"{topic}"});
            return result;
        }

        /// <summary>
        /// Registers this endpoint as a subscriber by creating a subscription for the given topic, setting up
        /// auto-forwarding from that subscription to this endpoint's input queue
        /// </summary>
        public async Task RegisterSubscriber(string subscriberAddress,
            string topic)
        {
            VerifyIsOwnInputQueueAddress(topic);

            topic = _nameFormatter.FormatTopicName(topic);

            _log.Debug("Registering subscription {subscriberAddress} for topic {topic}", subscriberAddress, topic);

            await _subscriptionExceptionIgnorant.Execute(async () =>
            {
                var topicProperties = await _azureTopicManager.EnsureTopicExists(topic).ConfigureAwait(false);
                var messageSender = GetMessageSender(Address);

                // var inputQueuePath = messageSender.EntityPath;
                var topicName = topicProperties.Name;

                var subscription = await _azureTopicManager.GetOrCreateSubscription(topicName, SubscriptionName, _settings).ConfigureAwait(false);
                // await _managementClient.UpdateSubscriptionAsync(subscription, _cancellationToken).ConfigureAwait(false);
                _log.Info("Subscription {subscriptionName} for topic {topicName} successfully registered", SubscriptionName, topic);
            }, _cancellationToken);
        }

        /// <summary>
        /// Unregisters this endpoint as a subscriber by deleting the subscription for the given topic
        /// </summary>
        public async Task UnregisterSubscriber(string subscriberAddress,
            string topic)
        {
            VerifyIsOwnInputQueueAddress(topic);

            topic = _nameFormatter.FormatTopicName(topic);

            _log.Debug("Unregistering subscription {subscriberAddress} for topic {topic}", subscriberAddress, topic);

            await _subscriptionExceptionIgnorant.Execute(async () =>
            {
                var topicProperties = await _azureTopicManager.EnsureTopicExists(topic).ConfigureAwait(false);
                var topicName = topicProperties.Name;

                try
                {
                    await _managementClient.DeleteSubscriptionAsync(topicName, SubscriptionName, _cancellationToken).ConfigureAwait(false);

                    _log.Info("Subscription {subscriptionName} for topic {topicName} successfully unregistered",
                        SubscriptionName, topic);
                }
                catch (ServiceBusException)
                {
                    // it's alright man
                }
            }, _cancellationToken);
        }

        void VerifyIsOwnInputQueueAddress(string subscriberAddress)
        {
            if (subscriberAddress == Address) return;

            var message = $"Cannot register subscriptions endpoint with input queue '{subscriberAddress}' in endpoint with input" +
                          $" queue '{Address}'! The Azure Service Bus transport functions as a centralized subscription" +
                          " storage, which means that all subscribers are capable of managing their own subscriptions";

            throw new ArgumentException(message);
        }


        /// <summary>
        /// Creates a queue with the given address
        /// </summary>
        public void CreateQueue(string address)
        {
            //TODO:buraya neden düşüyor hangi parametreyle öğrenmek lazım: error queue açmaya çalıştı
            if (address != this.Address)
            {
                address = this.Address + "_" + address;
            }

            address = _nameFormatter.FormatTopicName(address);

            _azureTopicManager.InnerCreateTopic(address);
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
                    return _nameFormatter.FormatTopicName(deferredRecipient);
                }
            }

            return _nameFormatter.FormatTopicName(destinationAddress);
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

            if (headers.TryGetValue(Headers.ContentType, out var contentType))
            {
                message.ContentType = contentType;
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
                var (parsedMessageId, parsedType) = MasstransitHelpers.SetMessageIdFromMasstransitMessage(
                    message.Body.ToArray(),_log);
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
                        var topicName = _nameFormatter.FormatTopicName(destinationQueue);
                        var topicClient = await GetTopicClient(topicName);
                        var serviceBusMessageBatches = await GetBatches(messages.Select(GetMessage), topicClient);

                        using (serviceBusMessageBatches.AsDisposable(b => b.DisposeCollection()))
                        {
                            foreach (var batch in serviceBusMessageBatches)
                            {
                                try
                                {
                                    await topicClient
                                        .SendMessagesAsync(batch, _cancellationToken)
                                        .ConfigureAwait(false);
                                }
                                catch (Exception exception)
                                {
                                    throw new RebusApplicationException(exception, $"Could not publish to topic '{topicName}'");
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
                catch (ServiceBusException exception) when (exception.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
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
            var receivedMessage = await ReceiveInternal().ConfigureAwait(false);

            if (receivedMessage == null) return null;

            var message = receivedMessage.Message;
            var messageReceiver = receivedMessage.MessageReceiver;

            var items = context.Items;

            // add the message and its receiver to the context
            items["asb-message"] = message;
            items["asb-message-receiver"] = messageReceiver;

            if (string.IsNullOrWhiteSpace(message.LockToken))
            {
                throw new RebusApplicationException($"OMG that's weird - message with ID {message.MessageId} does not have a lock token!");
            }

            var messageId = message.MessageId;

            // if (_settings.AutomaticPeekLockRenewalEnabled && !_prefetchingEnabled)
            // {
            //     _messageLockRenewers.TryAdd(message.MessageId, new MessageLockRenewer(message, messageReceiver));
            // }

            context.OnCompleted(async ctx =>
            {
                // only ACK the message if it's still in the context - this way, carefully crafted
                // user code can take over responsibility for the message by removing it from the transaction context
                // var shouldSkipComplete = ctx.Items["skip-complete"] as bool?;
                if (ctx.Items.TryGetValue("asb-message", out var messageObject)
                    && messageObject is ServiceBusReceivedMessage asbMessage
                    // && shouldSkipComplete != true
                )
                {
                    var lockToken = asbMessage.LockToken;

                    try
                    {
                        // ReSharper disable once MethodSupportsCancellation
                        await messageReceiver
                            .CompleteMessageAsync(asbMessage)
                            .ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        throw new RebusApplicationException(exception,
                            $"Could not complete message with ID {messageId} and lock token {lockToken}");
                    }
                }

                // _messageLockRenewers.TryRemove(messageId, out _);
            });

            context.OnAborted(ctx =>
            {
                // _messageLockRenewers.TryRemove(messageId, out _);

                AsyncHelpers.RunSync(async () =>
                {
                    // only NACK the message if it's still in the context - this way, carefully crafted
                    // user code can take over responsibility for the message by removing it from the transaction context
                    if (ctx.Items.TryGetValue("asb-message", out var messageObject)
                        && messageObject is ServiceBusReceivedMessage asbMessage)
                    {
                        var lockToken = asbMessage.LockToken;

                        try
                        {
                            await messageReceiver.AbandonMessageAsync(message, cancellationToken: _cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            throw new RebusApplicationException(exception,
                                $"Could not abandon message with ID {messageId} and lock token {lockToken}");
                        }
                    }
                });
            });

            // context.OnDisposed(ctx => _messageLockRenewers.TryRemove(messageId, out _));

            var applicationProperties = message.ApplicationProperties;
            var headers = applicationProperties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value?.ToString());
            var body = message.Body;

            return new TransportMessage(headers, body.ToMemory().ToArray());
        }

        async Task<ReceivedMessage> ReceiveInternal()
        {
            try
            {
                if (_messageReceiver == null)
                    throw new RebusApplicationException(
                        $"Could not receive next message from Azure Service Bus topic '{Address}', message receiver null?");
                if (_messageReceiver.IsClosed)
                {
                    _log.Error("Topic Message receiver was closed, recreating, why?");
                    _messageReceiver = MessageReceiverManager.RecreateTopicReceiver(_prefetchCount,
                        Address, SubscriptionName, _client, _log, _disposables);
                }

                var message = await _messageReceiver.ReceiveMessageAsync(_settings.ReceiveOperationTimeout, _cancellationToken).ConfigureAwait(false);
                return message == null
                    ? null
                    : new ReceivedMessage(message, _messageReceiver);
            }
            catch (ServiceBusException exception)
            {
                throw new RebusApplicationException(exception, $"Could not receive next message from Azure Service Bus topic '{Address}'");
            }
        }

        /// <summary>
        /// Gets the input queue name for this transport
        /// </summary>
        public string Address { get; }

        /// <summary>
        /// Initializes the transport by ensuring that the input queue has been created
        /// </summary>
        /// <inheritdoc />
        public void Initialize()
        {
            // _disposables.Push(_messageLockRenewalTask);

            if (Address == null)
            {
                throw new NotImplementedException("One way ASB topic is not implemented");
            }

            _log.Info("Initializing Azure Service Bus transport with topic {queueName}", Address);

            _azureTopicManager.InnerCreateTopic(Address);

            _azureTopicManager.CheckInputTopicConfiguration(Address);

            AsyncHelpers.RunSync(async () =>
            {
                await _azureTopicManager.GetOrCreateSubscription(Address, SubscriptionName, _settings).ConfigureAwait(false);
            });


            _messageReceiver = MessageReceiverManager.RecreateTopicReceiver(_prefetchCount,
                Address, SubscriptionName, _client, _log, _disposables);

            // if (_settings.AutomaticPeekLockRenewalEnabled)
            // {
            //     _messageLockRenewalTask.Start();
            // }
        }

        /// <summary>
        /// Always returns true because Azure Service Bus topics and subscriptions are global
        /// </summary>
        public bool IsCentralized => true;

        /// <summary>
        /// Configures the transport to prefetch the specified number of messages into an in-mem queue for processing, disabling automatic peek lock renewal
        /// </summary>
        public void PrefetchMessages(int prefetchCount)
        {
            if (prefetchCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(prefetchCount), prefetchCount, "Must prefetch zero or more messages");
            }

            _prefetchingEnabled = prefetchCount > 0;
            _prefetchCount = prefetchCount;
        }

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

        async Task<ServiceBusSender> GetTopicClient(string topic) => await _topicClients.GetOrAdd(topic, _ => new(async () =>
        {
            await _azureTopicManager.EnsureTopicExists(topic);

            var topicClient = _client.CreateSender(topic);

            _disposables.Push(topicClient.AsDisposable(t => AsyncHelpers.RunSync(async () =>
            {
                try
                {
                    await t.CloseAsync(_cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
                {
                    // it's ok
                }
            })));

            return topicClient;
        })).Value;

        // async Task RenewPeekLocks()
        // {
        //     var mustBeRenewed = _messageLockRenewers.Values
        //         .Where(r => r.IsDue)
        //         .ToList();
        //
        //     if (!mustBeRenewed.Any()) return;
        //
        //     _log.Debug("Found {count} peek locks to be renewed", mustBeRenewed.Count);
        //
        //     await Task.WhenAll(mustBeRenewed.Select(async r =>
        //     {
        //         try
        //         {
        //             await r.Renew().ConfigureAwait(false);
        //
        //             _log.Debug("Successfully renewed peek lock for message with ID {messageId}", r.MessageId);
        //         }
        //         catch (Exception exception)
        //         {
        //             _log.Warn(exception, "Error when renewing peek lock for message with ID {messageId}", r.MessageId);
        //         }
        //     }));
        // }
    }
}