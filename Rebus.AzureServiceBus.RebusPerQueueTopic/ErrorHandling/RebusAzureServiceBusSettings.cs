using System;
using Microsoft.AspNetCore.Hosting;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling
{
    public class RebusAzureServiceBusSettings
    {
        public TimeSpan[] RetryDelays { get; set; }
        
        public IWebHostEnvironment Environment { get; set; }
        
        
    }
}