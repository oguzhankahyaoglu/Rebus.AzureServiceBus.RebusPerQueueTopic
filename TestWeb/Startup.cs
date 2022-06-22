using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using ServiceBusConfigurator.TestWeb.StartupConfiguration;

namespace ServiceBusConfigurator.TestWeb
{
    public class Startup
    {
        public Startup(IWebHostEnvironment env,
            IConfiguration configuration)
        {
            Environment = env;
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }
        private IWebHostEnvironment Environment { get; }

        // This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            SerilogConfigurer.ConfigureServices(services, Environment, Configuration);
            services.AddControllers();
            services.AddSwaggerGen(c => { c.SwaggerDoc("v1", new OpenApiInfo {Title = "ServiceBusConfigurator.TestWeb", Version = "v1"}); });
            AzureServiceBusConfigurator.Register(services, Configuration, Environment);
            services.AddControllers()
                .AddNewtonsoftJson(options => { options.SerializerSettings.NullValueHandling = NullValueHandling.Ignore; });
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app,
            IWebHostEnvironment env,
            ILoggerFactory loggerFactory)
        {
            SerilogConfigurer.Configure(app, env, loggerFactory);
            AzureServiceBusConfigurator.Configure(app);
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
                app.UseSwagger();
                app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "ServiceBusConfigurator.TestWeb v1"));
            }

            app.UseHttpsRedirection();

            app.UseRouting();

            app.UseAuthorization();

            app.UseEndpoints(endpoints =>
            {
                HealthCheckResponseWriter.Configure(endpoints);
                endpoints.MapDefaultControllerRoute();
            });
        }
    }
}