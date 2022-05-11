using System;
using System.Linq;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    [StepDocumentation(@"Adjusts incoming message typename to the consumer project by searching type without NAMESPACEs")]
    public class IncomingMessageTypeHeaderAdjusterStep : IIncomingStep
    {
        private readonly ILog _log;
        private string Name => nameof(IncomingMessageTypeHeaderAdjusterStep);

        public IncomingMessageTypeHeaderAdjusterStep(ILog log)
        {
            _log = log;
        }

        public async Task Process(IncomingStepContext context,
            Func<Task> next)
        {
            var transportMessage = context.Load<TransportMessage>();
            //While sharing messages, since assembly names maybe different from incoming and outgoing projects
            //we are adjusting incoming type header to a mapped type within project
            if (!transportMessage.Headers.TryGetValue(Headers.Type, out var headerTypeStr))
            {
                _log.Warn("{Name} message does not contain Type header, so cannot adjust anything.", Name);
                await next();
                return;
            }

            var foundType = Type.GetType(headerTypeStr);
            if (foundType == null)
            {
                var fullNameStr = headerTypeStr.Split(",").First();
                foundType = MessageTypeFinder.FindType(fullNameStr);
                if (foundType == null)
                {
                    var typeStr = fullNameStr.Split(".").Last();
                    foundType = MessageTypeFinder.FindType(typeStr);
                    if (foundType == null)
                    {
                        _log.Warn("{Name} Cannot adjust fulltypename of '{headerTypeStr}', could not match to project's consumer types", Name,
                            headerTypeStr);
                    }
                    else
                    {
                        _log.Debug("{Name} adjusting '{headerTypeStr}' to '{newType}'", Name, headerTypeStr, foundType?.AssemblyQualifiedName);
                        transportMessage.Headers[Headers.Type] = foundType.AssemblyQualifiedName;
                        context.Save(transportMessage);
                    }
                }
                else
                {
                    _log.Debug("{Name} adjusting '{headerTypeStr}' to '{newType}' keeping its original namespace", Name, headerTypeStr,
                        foundType.AssemblyQualifiedName);
                    transportMessage.Headers[Headers.Type] = foundType.AssemblyQualifiedName;
                    context.Save(transportMessage);
                }
            }
            await next();
        }
    }
}