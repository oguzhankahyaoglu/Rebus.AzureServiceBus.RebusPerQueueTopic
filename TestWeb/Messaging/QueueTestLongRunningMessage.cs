using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.Handlers;
using Serilog;
using Serilog.Context;

namespace ServiceBusConfigurator.TestWeb.Messaging
{
    public class QueueTestLongRunningMessage
    {
        public const string QUEUE_NAME = "fernas-test-queue-long-running-message";

        public int Index { get; set; }
    }

    public class QueueTestLongRunningMessageHandler : IHandleMessages<QueueTestLongRunningMessage>
    {
        #region ctor & member

        private string Name => nameof(QueueTestLongRunningMessage);

        #endregion


        public async Task Handle(QueueTestLongRunningMessage runningMessage)
        {
            var guid = Guid.NewGuid();
            using (LogContext.PushProperty("Command", Name))
                try
                {
                    Log.Information($"[{Name}] {guid} {runningMessage.Index:0000} started");
                    var expireDate = DateTime.Now.AddMinutes(10);
                    while (DateTime.Now < expireDate)
                    {
                        await Task.Delay(1 * 1000);
                        Log.Information($"[{Name}] {guid} heartbeat, will end at {expireDate}");
                    }

                    Log.Information($"[{Name}] {guid} {runningMessage.Index:0000} CONSUMED");
                }
                catch (Exception ex)
                {
                    Log.Error($"[{Name} {guid} {runningMessage.Index:0000}] failed reverting", ex);
                    ex.LogMessageErrors(runningMessage);
                    throw;
                }
        }
    }
}