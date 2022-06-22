using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.Handlers;
using Serilog;
using Serilog.Context;

namespace TestWeb.Messaging
{
    public class QueueCompressTestMessage
    {
        public const string QUEUE_NAME = "QueueCompressTestMessage";

        public int Index { get; set; }
        public string Body { get; set; }
    }

    /// <summary>
    /// Aylık hakediş özeti ekranı; Aylık Kömür tutanak.veya Aylık Kireçtaşı Tutanak değişirse update edilmeli.
    /// </summary>
    public class QueueCompressTestMessageHandler : IHandleMessages<QueueCompressTestMessage>
    {
        #region ctor & member

        private string Name => nameof(QueueCompressTestMessage);

        #endregion


        public async Task Handle(QueueCompressTestMessage message)
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