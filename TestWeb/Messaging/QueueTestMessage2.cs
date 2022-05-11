using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.Handlers;
using Serilog;
using Serilog.Context;

namespace ServiceBusConfigurator.TestWeb.Messaging
{
    public class QueueTestMessage2
    {
        public const string QUEUE_NAME = "fernas-test-message-3";

        public int Index { get; set; }
    }

    /// <summary>
    /// Aylık hakediş özeti ekranı; Aylık Kömür tutanak.veya Aylık Kireçtaşı Tutanak değişirse update edilmeli.
    /// </summary>
    public class QueueTestMessage2Handler : IHandleMessages<QueueTestMessage2>
    {
        #region ctor & member

        private string Name => nameof(QueueTestMessage2);

        #endregion


        public async Task Handle(QueueTestMessage2 message)
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