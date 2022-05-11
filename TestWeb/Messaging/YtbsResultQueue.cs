using System;
using System.Threading.Tasks;
using Rebus.Handlers;

namespace TeiasOsosApi.Integration.Events
{
    // [QueueMessageName(QUEUE_NAME)]
    public class YtbsResultCommand
    {
        public const string QUEUE_NAME = "ytbs-results-queue";

        public Guid Id { get; set; }
        public string Source { get; set; }
        public string Date { get; set; }
        public string Time { get; set; }
        public bool IsSucceed { get; set; }
        public bool IsCompleted { get; set; }
        public bool IsDuplicated { get; set; }
        public string TimeStamp { get; set; }
        public string ErrorMessage { get; set; }
        public string ScreenshotLink { get; set; }
        public string PowerPlant { get; set; }
        public string Data { get; set; }
        public string UserId { get; set; }
        public string YtbsUserName { get; set; }
    }

    public class YtbsResultHandler : IHandleMessages<YtbsResultCommand>
    {
        public Task Handle(YtbsResultCommand message)
        {
            //throw new NotImplementedException();
            return Task.CompletedTask;
        }
    }
}