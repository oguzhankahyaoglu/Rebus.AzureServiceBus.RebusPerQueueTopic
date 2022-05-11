using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.Bus;
using Rebus.Exceptions;
using Rebus.Handlers;
using Rebus.Messages;
using Rebus.Retry.Simple;
using Serilog;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator
{
    public class AzureDeferSecondLevelRetryHandler<TMessage> : IHandleMessages<IFailed<TMessage>>
    {
        private readonly IBus _bus;
        private readonly RebusAzureServiceBusSettings _retrySettings;

        public AzureDeferSecondLevelRetryHandler(IBus bus, RebusAzureServiceBusSettings retrySettings)
        {
            _bus = bus;
            _retrySettings = retrySettings;
        }

        public async Task Handle(IFailed<TMessage> message)
        {
            var deferCount = Convert.ToInt32(message.Headers.GetValueOrDefault(Headers.DeferCount));
            if (deferCount >= _retrySettings.RetryDelays.Length)
            {
                var errorDetails = $"Failed after {deferCount} deferrals, max allowed {_retrySettings.RetryDelays.Length}: {message.ErrorDescription}";
                Log.Warning(errorDetails);
                await _bus.Advanced.TransportMessage.Deadletter(errorDetails);
                return;
            }

            var deferDelay = _retrySettings.RetryDelays[deferCount];
            Log.Warning($"Deferring message for {deferDelay.TotalSeconds:N0} seconds, {deferCount+1}th retry, max allowed {_retrySettings.RetryDelays.Length}: {message.ErrorDescription}");
            await _bus.Advanced.TransportMessage.Defer(deferDelay);
        }
    }
}