using System;
using Microsoft.AspNetCore.Mvc;
using Serilog;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BusCommonController : Controller
    {
        [HttpGet(nameof(RestartApp))]
        public void RestartApp()
        {
            Log.Error("Restart application called, restarting");
            Environment.Exit(123123);
        }
    }
}