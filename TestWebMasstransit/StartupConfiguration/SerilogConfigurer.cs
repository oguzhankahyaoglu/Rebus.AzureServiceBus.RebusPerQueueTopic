using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Formatting.Elasticsearch;

namespace ServiceBusConfigurator.TestWebMasstransit.StartupConfiguration
{
    public static class SerilogConfigurer
    {
        private static readonly Type[] IgnoredExceptions =
        {
            typeof(TaskCanceledException),
            typeof(AntiforgeryValidationException)
        };

        private static Logger CreateLoggerConfig(IConfiguration appConfiguration, IWebHostEnvironment environment)
        {
            Serilog.Debugging.SelfLog.Enable(s => Debug.Write(s));
            var loggerConfiguration = new LoggerConfiguration()
                .ReadFrom.Configuration(appConfiguration)
                .Filter
                .ByExcluding(logEvent =>
                    logEvent.Exception != null &&
                    (
                        IgnoredExceptions.Contains(logEvent.Exception.GetType())
                    )
                );
            if (environment.IsDevelopment())
            {
                var debugTemplate =
                    "[{Timestamp:HH:mm:ss} {Level:u3} {Integration}-{Source} {DataDate} {DurationSeconds}] {Message:lj}{NewLine}{Exception}";
                loggerConfiguration = loggerConfiguration
                    .WriteTo.ColoredConsole(outputTemplate: debugTemplate);
            }
            else
            {
                loggerConfiguration = loggerConfiguration.WriteTo.Console(new ElasticsearchJsonFormatter());
            }

            var config = loggerConfiguration.CreateLogger();
            return config;
        }

        public static void ConfigureServices(IServiceCollection services, IWebHostEnvironment webHostEnvironment, IConfiguration configuration)
        {
            services.AddLogging(x => x.ClearProviders());
            var logger = CreateLoggerConfig(configuration, webHostEnvironment);
            Log.Logger = logger;
            services.AddSingleton(Log.Logger);
        }

        public static void Configure(IApplicationBuilder app, IWebHostEnvironment env, ILoggerFactory loggerFactory)
        {
            loggerFactory.AddSerilog();
        }
    }
}