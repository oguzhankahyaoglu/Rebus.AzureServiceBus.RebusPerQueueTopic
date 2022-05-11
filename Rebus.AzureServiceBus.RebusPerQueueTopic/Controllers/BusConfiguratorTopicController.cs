using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class BusConfiguratorTopicController : Controller
    {
        private readonly IConfiguration _config;

        public BusConfiguratorTopicController(IConfiguration config)
        {
            _config = config;
        }

        [HttpGet(nameof(TopicRemoveSubscription))]
        public async Task<string> TopicRemoveSubscription(string topicName,
            string subscriptionName)
        {
            var serviceBusConnectionString = _config.GetValue<string>("ServiceBus:ConnectionString");
            await ManagementExtensions.RemoveTopicSubscription(serviceBusConnectionString,
                topicName, subscriptionName);
            return "Removed";
        }


        [HttpGet(nameof(RemoveTopic))]
        public async Task<string> RemoveTopic(string topicName)
        {
            var serviceBusConnectionString = _config.GetValue<string>("ServiceBus:ConnectionString");
            await ManagementExtensions.RemoveTopic(serviceBusConnectionString, topicName);
            return "Removed";
        }


        [HttpGet(nameof(RemoveAllSubscriptions))]
        public async Task<string> RemoveAllSubscriptions(string topicName)
        {
            var serviceBusConnectionString = _config.GetValue<string>("ServiceBus:ConnectionString");
            await ManagementExtensions.RemoveAllSubscriptions(serviceBusConnectionString, topicName);
            return "Removed";
        }
    }
}