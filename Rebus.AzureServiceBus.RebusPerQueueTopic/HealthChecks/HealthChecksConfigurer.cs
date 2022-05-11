using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.HealthChecks
{
    public static class HealthChecksConfigurer
    {
         public static void AddSqlServerHealthChecks(IServiceCollection services,
             string sqlConnectionString)
         {
             var timeout = TimeSpan.FromSeconds(30);
             if (string.IsNullOrEmpty(sqlConnectionString))
             {
                 Log.Warning("Skipping sql server health check since connstr null!");
                 return;
             }
        
             services.AddHealthChecks().AddSqlServer(sqlConnectionString, timeout: timeout);
         }

        public static void AddBusHealthChecks(IServiceCollection services,
            IWebHostEnvironment env, string serviceBusConnectionString, BusHealthCheckOptions options)
        {
            RebusHealthChecks.AppStartupTime = DateTime.Now;
            Log.Information("Registering health checks for Rebus. App start time: {time}, initial warmup delay: {delay}",
                RebusHealthChecks.AppStartupTime, options.WarmupIgnoreDelayForHealthChecking);
            if (env.IsDevelopment())
            {
                options.WarmupIgnoreDelayForHealthChecking = TimeSpan.FromSeconds(1);
                Log.Warning("{opt} Changing WarmupIgnoreDelayForHealthChecking to 1sec due dev mode", nameof(BusHealthCheckOptions));
            }
            services.AddSingleton(options);
            services.AddHealthChecks()
                .AddAsyncCheck(nameof(RebusHealthChecks), async () =>
                {
                    var check = new RebusHealthChecks(serviceBusConnectionString, options);
                    return await check.CheckHealthAsync();
                });
        }
    }
}