using System;

namespace TeiasOsosApi.Integration.Events
{
    public class TeiasOsosApiDataChangedEvent
    {
        public const string QUEUE_NAME = "semih.guzelel/teias-osos-api-data-changed-queue";
        public Guid Id { get; set; }
        public DateTime Timestamp { get; set; }
        public DateTime Date { get; set; }
        public string[] EtsoCodes { get; set; }
    }
}