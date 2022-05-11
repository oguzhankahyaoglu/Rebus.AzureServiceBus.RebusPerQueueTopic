using System;
using System.Threading.Tasks;
using Rebus.Handlers;
using Serilog;

namespace ServiceBusConfigurator.TestWeb.Messaging
{
    public class MasstransitToRebusQueueMessage
    {
        public const string QUEUE_NAME = "fernas-masstransit-to-rebus-test";
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
    }

    public class MasstransitToRebusQueueMessageHandler : IHandleMessages<MasstransitToRebusQueueMessage>
    {
        private string Name => nameof(MasstransitToRebusQueueMessage);
        
        public async Task Handle(MasstransitToRebusQueueMessage message)
        {
            Log.Information("{Name} Message received: {date}", Name, message?.Date);
            await Task.Delay(15 * 1000);
            Log.Information("{Name} Message consume finished.", Name);
        }
    }
}