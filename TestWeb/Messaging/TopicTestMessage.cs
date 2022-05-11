using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.Handlers;
using Serilog;
using Serilog.Context;

namespace ServiceBusConfigurator.TestWeb.Messaging
{
    public class TopicTestMessage
    {
        public const string QUEUE_NAME = "fernas-test-topic-message-11";

        public int Index { get; set; }
    }

    public class TopicTestMessageHandler : IHandleMessages<TopicTestMessage>
    {
        #region ctor & member

        private string Name => nameof(TopicTestMessage);

        #endregion


        public async Task Handle(TopicTestMessage message)
        {
            if (message == null)
            {
                Log.Error("Message is null, skipping.");

                return;
            }

            using (LogContext.PushProperty("Command", Name))
                try
                {
                    await Task.Delay(10);
                    Log.Information($"[{Name} {message.Index:0000}] CONSUMED");
                }
                catch (Exception ex)
                {
                    Log.Error($"[{Name} {message.Index:0000}] failed reverting", ex);
                    ex.LogMessageErrors(message);
                    throw;
                }
        }
    }
}