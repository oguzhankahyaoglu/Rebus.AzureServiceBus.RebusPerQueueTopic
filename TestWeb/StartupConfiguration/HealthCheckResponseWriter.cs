using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Newtonsoft.Json;

namespace ServiceBusConfigurator.TestWeb.StartupConfiguration
{
    public static class HealthCheckResponseWriter
    {
        private const string READINESS = "readiness";
        private const string LIVENESS = "liveness";

        public static void Configure(IEndpointRouteBuilder endpoints)
        {
            var resultStatusCodes = new Dictionary<HealthStatus, int>
            {
                {HealthStatus.Healthy, 200},
                {HealthStatus.Degraded, 500},
                {HealthStatus.Unhealthy, 500},
            };
            endpoints.MapHealthChecks($"/health/{LIVENESS}", new HealthCheckOptions()
            {
                ResponseWriter = WriteResponse,
                AllowCachingResponses = false,
                ResultStatusCodes = resultStatusCodes,
                Predicate = null
            });

            endpoints.MapHealthChecks($"/health/{READINESS}", new HealthCheckOptions()
            {
                ResponseWriter = WriteResponse,
                AllowCachingResponses = false,
                ResultStatusCodes = resultStatusCodes,
                Predicate = (check) => check.Tags.Contains(LIVENESS),
            });
        }

        private static Task WriteResponse(HttpContext context,
            HealthReport result)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            var jsonSerializerSettings = new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            };
            jsonSerializerSettings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
            return context.Response.WriteAsync(JsonConvert.SerializeObject(result, Formatting.Indented, jsonSerializerSettings));
        }
    }
}