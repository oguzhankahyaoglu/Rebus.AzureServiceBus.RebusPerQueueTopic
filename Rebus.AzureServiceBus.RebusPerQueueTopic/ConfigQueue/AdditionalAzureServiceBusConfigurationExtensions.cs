﻿using System;
using System.Threading.Tasks;
using Azure.Messaging.ServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Retry;
using Rebus.Transport;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue
{
    /// <summary>
    /// Experimental configuration extensions for changing the way dead-lettering works with Rebus
    /// </summary>
    public static class AdditionalAzureServiceBusConfigurationExtensions
    {
        /// <summary>
        /// Extends Rebus' built-in deadlettering with the ability to use Azure Service Bus' built-in deadlettering
        /// </summary>
        public static void UseNativeDeadlettering(this StandardConfigurer<ITransport> configurer)
        {
            configurer
                .OtherService<IErrorHandler>()
                .Decorate(c => new BuiltInDeadletteringErrorHandler(c.Get<IErrorHandler>(), c.Get<IRebusLoggerFactory>()));
        }

        class BuiltInDeadletteringErrorHandler : IErrorHandler
        {
            readonly IErrorHandler _errorHandler;
            private readonly ILog _log;

            public BuiltInDeadletteringErrorHandler(IErrorHandler errorHandler, IRebusLoggerFactory rebusLoggerFactory)
            {
                _errorHandler = errorHandler ?? throw new ArgumentNullException(nameof(errorHandler));
                _log = rebusLoggerFactory?.GetLogger<BuiltInDeadletteringErrorHandler>() ?? throw new ArgumentNullException(nameof(rebusLoggerFactory));
            }

            public async Task HandlePoisonMessage(TransportMessage transportMessage, ITransactionContext transactionContext, Exception exception)
            {
                if (transactionContext.Items.TryGetValue("asb-message", out var messageObject)
                    && messageObject is ServiceBusReceivedMessage message
                    && transactionContext.Items.TryGetValue("asb-message-receiver", out var messageReceiverObject)
                    && messageReceiverObject is ServiceBusReceiver messageReceiver)
                {
                    const int headerValueMaxLength = 4096;

                    var deadLetterReason = exception.Message.TrimTo(maxLength: headerValueMaxLength);
                    var deadLetterErrorDescription = exception.ToString().TrimTo(maxLength: headerValueMaxLength);
                    var lockToken = message.LockToken;

                    if (!transportMessage.Headers.TryGetValue(Headers.MessageId, out var messageId))
                    {
                        messageId = "<unknown>";
                    }

                    _log.Error(exception, "Dead-lettering message with ID {messageId}, reason={deadLetterReason}", messageId, deadLetterReason);
                    await messageReceiver.DeadLetterMessageAsync(message, deadLetterReason, deadLetterErrorDescription);

                    // remove the message from the context, so the transport doesn't try to complete the message
                    transactionContext.Items.TryRemove("asb-message", out _);
                }
                else
                {
                    await _errorHandler.HandlePoisonMessage(transportMessage, transactionContext, exception);
                }
            }
        }
    }
}