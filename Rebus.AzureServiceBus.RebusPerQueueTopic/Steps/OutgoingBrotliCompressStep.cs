using System;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Unicode;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rebus.AzureServiceBus.RebusPerQueueTopic.MassTransit;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Serializers;
using Rebus.Extensions;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Serialization;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    [StepDocumentation(@"Tries to compress message body using Brotli algorithm")]
    public class OutgoingBrotliCompressStep : IOutgoingStep
    {
        private readonly ILog _logger;
        private readonly ISerializer _serializer;

        public OutgoingBrotliCompressStep(ILog logger, ISerializer serializer)
        {
            _logger = logger;
            _serializer = serializer;
        }

        private static readonly Encoding DefaultEncoding = Encoding.UTF8;
     
        public async Task Process(OutgoingStepContext context,
            Func<Task> next)
        {
            var oldMessage = context.Load<TransportMessage>();
            var oldMessageBody = await _serializer.Deserialize(oldMessage);
            var oldMessageBodyJson = JsonConvert.SerializeObject(oldMessageBody.Body, new JsonSerializerSettings
            {
                Formatting = Formatting.None
            });
            var compressed = await oldMessageBodyJson.ToBrotliAsync(CompressionLevel.Optimal);
            oldMessage.Headers["rbs2-compressed"] = "brotli";
            var compressedBytes = Encoding.UTF8.GetBytes(compressed.Result.Value);
            var newMessage = new TransportMessage(oldMessage.Headers.Clone(), compressedBytes);
            context.Save(newMessage);
            _logger.Debug($"Brotli Reduced from {oldMessageBodyJson.Length} str to {compressedBytes.Length}");
            await next();
        }
        
        private Encoding GetEncoding(string contentType)
        {
            var strArray = contentType.Split(';')
                .Select((Func<string, string[]>)(token => token.Split('=')))
                .Where((Func<string[], bool>)(tokens => tokens.Length == 2))
                .FirstOrDefault((Func<string[], bool>)(tokens => tokens[0] == "charset"));
            if (strArray == null)
                return DefaultEncoding;
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