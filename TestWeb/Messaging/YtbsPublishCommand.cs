using System;
using System.Threading.Tasks;
using Rebus.Handlers;
using Serilog;

namespace TeiasOsosApi.Integration.Events
{
    public class YtbsPublishCommand
    {
        public const string QUEUE_NAME = "ytbs-queue";

        public Guid Id { get; set; }
        public bool CanSave { get; set; }
        public bool GetPuantHours { get; set; }
        public bool IsPuantData { get; set; }
        public string Source { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public string PowerPlant { get; set; }
        public string UserId { get; set; }
        public string YtbsUserName { get; set; }
        public string[] Data { get; set; }
    }

    public class YtbsPublishCommandHandler : IHandleMessages<YtbsPublishCommand>
    {
        private string Name => nameof(YtbsPublishCommand);

        public async Task Handle(YtbsPublishCommand message)
        {
            Log.Information("{Name} Message received: {date} {id}", Name, message?.Date, message?.Id);
            await Task.Delay(15 * 1000);
            Log.Information("{Name} Message consume finished.", Name);
        }
    }
}