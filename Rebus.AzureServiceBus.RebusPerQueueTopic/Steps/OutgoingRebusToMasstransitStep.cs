using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.MassTransit;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    [StepDocumentation(@"Tries to adjust message to Masstransit acceptable format")]
    public class OutgoingRebusToMasstransitStep : IOutgoingStep
    {
        private readonly ILog _logger;

        public OutgoingRebusToMasstransitStep(ILog logger)
        {
            _logger = logger;
        }

        public async Task Process(OutgoingStepContext context,
            Func<Task> next)
        {
            var transportMessage = context.Load<TransportMessage>();
            var newMessage = new RebusToMasstransitMessageEnvelope(transportMessage, _logger);
            var newTransportMessage = newMessage.GetTransportMessage();
            context.Save(newTransportMessage);
            await next();
        }
    }
}