using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator
{
    public static class DevelopmentQueueNameHelper
    {
        public static string GetPrefix(IWebHostEnvironment environment)
        {
            var prefix = "";
            if (environment.IsDevelopment())
            {
                var userName = Environment.GetEnvironmentVariable("WINUSER") ?? Environment.UserName;
                prefix = $"{userName}/";
            }

            return prefix;
        }
    }
}