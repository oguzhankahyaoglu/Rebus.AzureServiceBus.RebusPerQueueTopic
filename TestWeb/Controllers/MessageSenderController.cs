using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces;
using TeiasOsosApi.Integration.Events;

namespace ServiceBusConfigurator.TestWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class MessageSenderController : ControllerBase
    {
        private readonly IMessageBrokerProvider _messageBrokerProvider;

        public MessageSenderController(IMessageBrokerProvider messageBrokerProvider)
        {
            _messageBrokerProvider = messageBrokerProvider;
        }

        [HttpGet("SendTeiasChangedEvent")]
        public async Task SendTeiasChangedEvent()
        {
            await _messageBrokerProvider.Send(new TeiasOsosApiDataChangedEvent
            {
                Date = DateTime.Today,
                Id = Guid.NewGuid(),
                Timestamp = DateTime.Now,
                EtsoCodes = new[] { "TEST1", "TEST2" }
            });
        }

        [HttpGet("PublishYtbsData")]
        public async Task PublishYtbsData()
        {
            await _messageBrokerProvider.Send(new YtbsPublishCommand
            {
                Id = Guid.NewGuid(),
                Date = DateTime.Today.ToString("yyyy-MM-dd")
            });
        }
    }
}