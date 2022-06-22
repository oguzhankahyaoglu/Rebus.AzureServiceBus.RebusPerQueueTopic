using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Serializers;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    [StepDocumentation(@"Decompresses incoming message body using Brotli algorithm")]
    public class IncomingBrotliCompressionStep : IIncomingStep
    {
        private readonly ILog _log;
        private string Name => nameof(IncomingBrotliCompressionStep);

        public IncomingBrotliCompressionStep(ILog log)
        {
            _log = log;
        }

        public async Task Process(IncomingStepContext context,
            Func<Task> next)
        {
            var transportMessage = context.Load<TransportMessage>();
            //While sharing messages, since assembly names maybe different from incoming and outgoing projects
            //we are adjusting incoming type header to a mapped type within project
            if (transportMessage.Headers.GetValueOrNull("rbs2-compressed") != "brotli")
            {
                _log.Debug("{Name} message does not contain compression header, skipping", Name);
                await next();
                return;
            }

            Encoding encoding;
            var contentType = transportMessage.Headers.GetValue("rbs2-content-type");
            if (contentType.Equals("application/json;charset=utf-8", StringComparison.OrdinalIgnoreCase))
            {
                encoding = Encoding.UTF8;
            }
            else
            {
                encoding = contentType.StartsWith("application/json")
                    ? GetEncoding(contentType)
                    : throw new FormatException("Unknown content type: '" + contentType +
                                                "' - must be 'application/json' (e.g. 'application/json;charset=utf-8') for the JSON serialier to work");
            }
            var messageBodyStr = encoding.GetString(transportMessage.Body);
            var decompressedStr = await messageBodyStr.FromBrotliAsync();
            var newBytes = encoding.GetBytes(decompressedStr);
            context.Save(new TransportMessage(transportMessage.Headers.Clone(),newBytes));
            await next();
        }
        
        private Encoding GetEncoding(string contentType)
        {
            var strArray = contentType.Split(';')
                .Select((Func<string, string[]>)(token => token.Split('=')))
                .Where((Func<string[], bool>)(tokens => tokens.Length == 2))
                .FirstOrDefault((Func<string[], bool>)(tokens => tokens[0] == "charset"));
            if (strArray == null)
                return Encoding.UTF8;
            var name = strArray[1];
            try
            {
                return Encoding.GetEncoding(name);
            }
            catch (Exception ex)
            {
                throw new FormatException("Could not turn charset '" + name + "' into proper encoding!", ex);
            }
        }


    }
}