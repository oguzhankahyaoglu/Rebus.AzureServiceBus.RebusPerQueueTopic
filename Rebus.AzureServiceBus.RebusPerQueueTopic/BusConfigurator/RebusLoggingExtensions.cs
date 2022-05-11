using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Serilog;
using Serilog.Context;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.BusConfigurator
{
    public static class RebusLoggingExtensions
    {
        /// <summary>Converts given object to JSON string.</summary>
        /// <returns></returns>
        private static string ToJsonString(object obj,
            bool camelCase = false,
            bool indented = false)
        {
            var settings = new JsonSerializerSettings
            {
                ContractResolver = new DefaultContractResolver
                {
                    NamingStrategy = camelCase ? new CamelCaseNamingStrategy() : new DefaultNamingStrategy()
                },
                Formatting = indented ? Formatting.Indented : Formatting.None,
                ReferenceLoopHandling = ReferenceLoopHandling.Ignore
            };

            return JsonConvert.SerializeObject(obj, settings);
        }

        public static void LogMessageErrors(this Exception e,
            object message)
        {
            using (LogContext.PushProperty("QueueMsg", ToJsonString(message)))
            {
                Log.Error(e, e.Message);
            }
        }
    }
}