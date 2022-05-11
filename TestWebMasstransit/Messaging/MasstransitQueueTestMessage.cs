using System;
using System.Threading.Tasks;
using MassTransit;
using Serilog;
using ServiceBusConfigurator.Attributes;

// ReSharper disable once CheckNamespace
namespace Fernas.Business.ServiceBus
{
    [QueueMessageName(QUEUE_NAME)]
    public class MasstransitQueueTestMessage
    {
        public const string QUEUE_NAME = "fernas-masstransit-queue-test";
        public Guid Id { get; set; }
        public DateTime Date { get; set; }
    }

    [QueueConsumer(MasstransitQueueTestMessage.QUEUE_NAME)]
    public class MasstransitQueueTestMessageHandler : IConsumer<MasstransitQueueTestMessage>
    {
        private string Name => nameof(MasstransitQueueTestMessage);

        public async Task Consume(ConsumeContext<MasstransitQueueTestMessage> context)
        {
            Log.Information("{Name} Message received: {date}", Name, context.Message?.Date);
            await Task.Delay(15 * 1000);
            Log.Information("{Name} Message consume finished.", Name);
        }
    }
}