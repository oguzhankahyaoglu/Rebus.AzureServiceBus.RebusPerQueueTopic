using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator;
using Rebus.Handlers;
using Serilog;
using Serilog.Context;

namespace ServiceBusConfigurator.TestWeb.Messaging
{
    public class QueueTestMessage
    {
        public const string QUEUE_NAME = "fernas-test-message-25";

        public int Index { get; set; }
        public bool Lock { get; set; }
    }

    /// <summary>
    /// Aylık hakediş özeti ekranı; Aylık Kömür tutanak.veya Aylık Kireçtaşı Tutanak değişirse update edilmeli.
    /// </summary>
    public class QueueTestMessageHandler : IHandleMessages<QueueTestMessage>
    {
        #region ctor & member

        private string Name => nameof(QueueTestMessage);

        #endregion


        public async Task Handle(QueueTestMessage message)
        {
            if (message == null)
            {
                Log.Error("Message is null, skipping.");

                return;
            }


            using (LogContext.PushProperty("Command", Name))
                try
                {
                    if (message.Lock)
                    {
                        var endDate = DateTime.Now.AddMinutes(7);
                        // while (DateTime.Now < endDate)
                        // {
                        //     await Task.Delay(15 * 1000);
                        // }
                        throw new NotImplementedException(DateTime.Now.ToString());
                    }

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