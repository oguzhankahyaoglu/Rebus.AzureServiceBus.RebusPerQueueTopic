using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using ServiceBusConfigurator.TestWebMasstransit.Messaging;

namespace ServiceBusConfigurator.TestWebMasstransit.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestMasstransitController : Controller
    {
        private readonly IMessageBrokerProvider _messageBrokerProvider;

        public TestMasstransitController(IMessageBrokerProvider messageBrokerProvider)
        {
            _messageBrokerProvider = messageBrokerProvider;
        }
        
        [HttpPost(nameof(MasstransitToRebusQueueMessage))]
        public async Task MasstransitToRebusQueueMessage([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new MasstransitToRebusQueueMessage
                {
                    Date = DateTime.Now,
                    Id = Guid.NewGuid()
                })));
        }
    }
}