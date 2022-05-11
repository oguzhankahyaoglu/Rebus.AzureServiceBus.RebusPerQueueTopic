using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.Handlers;
using Serilog;
using Serilog.Context;

namespace ServiceBusConfigurator.TestWeb.Messaging
{
    public class TopicTestErrorMessage
    {
        public const string QUEUE_NAME = "fernas-test-topic-error-message-3";

        public int Index { get; set; }
    }

    public class TopicTestErrorMessageHandler : IHandleMessages<TopicTestErrorMessage>
    {
        #region ctor & member

        private string Name => nameof(TopicTestErrorMessage);

        #endregion


        public async Task Handle(TopicTestErrorMessage message)
        {
            if (message == null)
            {
                Log.Error("Message is null, skipping.");

                return;
            }

            using (LogContext.PushProperty("Command", Name))
                try
                {
                    Log.Information($"[{Name}] Started excuting waiting 10secs");
                    await Task.Delay(10 * 1000);
                    throw new NotImplementedException(DateTime.Now.ToString());
                }
                catch (Exception ex)
                {
                    ex.LogMessageErrors(message);
                    throw;
                }
        }
    }
}