using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Fernas.Business.ServiceBus;
using Microsoft.AspNetCore.Mvc;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Interfaces;
using ServiceBusConfigurator.TestWeb.Messaging;
using TestWeb.Messaging;

namespace ServiceBusConfigurator.TestWeb.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TestRebusController : Controller
    {
        private readonly IMessageBrokerProvider _messageBrokerProvider;

        public TestRebusController(
            IMessageBrokerProvider messageBrokerProvider)
        {
            _messageBrokerProvider = messageBrokerProvider;
        }

        [HttpPost(nameof(QueueCompressTestMessage))]
        public async Task QueueCompressTestMessage([FromBody]string body)
        {
            await _messageBrokerProvider.Send(new QueueCompressTestMessage
            {
                Body = body
            });
        }

        [HttpPost(nameof(SendMasstransitQueueTestMessage))]
        public async Task SendMasstransitQueueTestMessage([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new MasstransitQueueTestMessage
                {
                    Date = DateTime.Now,
                })));
        }

        [HttpPost(nameof(SendMasstransitTopicTestMessage))]
        public async Task SendMasstransitTopicTestMessage([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new MasstransitTopicTestMessage
                {
                    Date = DateTime.Now,
                    Id = Guid.NewGuid()
                })));
        }

        [HttpPost(nameof(Enqueue))]
        public async Task Enqueue([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new QueueTestMessage
                {
                    Index = i,
                })));
        }

        [HttpPost(nameof(Enqueue2))]
        public async Task Enqueue2([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new QueueTestMessage2
                {
                    Index = i
                })));
        }

        [HttpPost(nameof(EnqueueLock))]
        public Task EnqueueLock()
        {
            return _messageBrokerProvider.Send(new QueueTestMessage
            {
                Index = 12345678,
                Lock = true
            });
        }

        [HttpPost(nameof(QueueOneWay))]
        public async Task QueueOneWay([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new QueueTestOneWayMessage
                {
                    Index = i
                })));
        }

        [HttpPost(nameof(EnqueueTopic))]
        public async Task EnqueueTopic([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new TopicTestMessage
                {
                    Index = i
                })));
        }

        [HttpPost(nameof(EnqueueError))]
        public async Task EnqueueError([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new TopicTestErrorMessage
                {
                    Index = i
                })));
        }

        [HttpPost(nameof(QueueTestLongRunningMessage))]
        public async Task QueueTestLongRunningMessage([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new QueueTestLongRunningMessage
                {
                    Index = i
                })));
        }

        [HttpPost(nameof(EnqueueTopicOneWay))]
        public async Task EnqueueTopicOneWay([FromQuery] int count)
        {
            await Task.WhenAll(Enumerable.Range(1, count)
                .Select(i => _messageBrokerProvider.Send(new TopicTestOneWayMessage
                {
                    Index = i
                })));
        }
    }
}