using System;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Bus;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Transport;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    [StepDocumentation(@"Limits message execution lifetime with 5m which is the maximum for azure servicebus")]
    public class IncomingMessageExecutionTimeoutWrappingStep : IIncomingStep
    {
        private readonly ILog _log;
        private readonly RebusAzureServiceBusSettings _settings;
        private string Name => nameof(IncomingMessageExecutionTimeoutWrappingStep);

        internal IncomingMessageExecutionTimeoutWrappingStep(ILog log,
            RebusAzureServiceBusSettings settings)
        {
            _log = log;
            _settings = settings;
        }

        public async Task Process(IncomingStepContext context,
            Func<Task> next)
        {
            var transportMessage = context.Load<TransportMessage>();
            var transactionContext = context.Load<ITransactionContext>();
            //While sharing messages, since assembly names maybe different from incoming and outgoing projects
            //we are adjusting incoming type header to a mapped type within project
            var timeout = new TimeSpan(0, 4, 50);
              try
            {
                await AsyncHelpers.CancelAfterAsync(async ct =>
                {
                    // _log.Debug("{Name} Message handle started {message}", Name, transportMessage.GetMessageLabel());
                    await next();
                    // _log.Debug("{Name} Message handle finished {message}", Name, transportMessage.GetMessageLabel());
                    //await transactionContext.Commit();
                }, timeout);
            }
            catch (RebusExecutionTimeoutException e)
            {
                _log.Error(e, "{Name} Message execution failed after {timeout}, {message} aborting message", Name, timeout,
                    transportMessage.GetMessageLabel());
                transactionContext.Abort();
            }
        }
    }
}