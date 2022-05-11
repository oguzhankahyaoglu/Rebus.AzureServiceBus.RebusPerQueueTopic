using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Rebus.Logging;
using Rebus.Messages;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.MassTransit
{
    public class RebusToMasstransitMessageEnvelope
    {
        private readonly ILog _logger;

        public string MessageId { get; }

        // public string RequestId { get; }
        public string CorrelationId { get; }

        // public string ConversationId { get; }
        // public string InitiatorId { get; }
        // public string SourceAddress { get; }
        // public string DestinationAddress { get; }
        // public string ResponseAddress { get; }
        // public string FaultAddress { get; }
        public string[] MessageType { get; }

        public object Message { get; set; }
        // public DateTime? ExpirationTime { get; }
        // public DateTime? SentTime { get; }
        // public Dictionary<string, object> Headers { get; }
        // public HostInfo Host { get; }

        private TransportMessage RebusMessage { get; }

        public RebusToMasstransitMessageEnvelope(TransportMessage rebusMessage,
            ILog logger)
        {
            _logger = logger;
            RebusMessage = rebusMessage;
            MessageId = TryGetHeaderValue(Rebus.Messages.Headers.MessageId);
            CorrelationId = TryGetHeaderValue(Rebus.Messages.Headers.CorrelationId);
            // SourceAddress = TryGetHeaderValue(Rebus.Messages.Headers.SenderAddress);
            // DestinationAddress = TryGetHeaderValue(Rebus.Messages.Headers.ReturnAddress);
            MessageType = new[] {GetUrnTypeName()};
            // Headers = rebusMessage.Headers
            //     .ToDictionary(k => k.Key, k => (object) k.Value);
            // Host = new HostInfo
            // {
            // };
        }

        public TransportMessage GetTransportMessage()
        {
            var rebusJsonStr = Encoding.UTF8.GetString(RebusMessage.Body);
            var rebusObj = JsonConvert.DeserializeObject(rebusJsonStr, AzureRebusCommon.NewtonsoftJsonSettings);
            this.Message = rebusObj;

            var envelopeJson = JsonConvert.SerializeObject(this, Formatting.None, AzureRebusCommon.NewtonsoftJsonSettings);
            var body = Encoding.UTF8.GetBytes(envelopeJson);
            return new TransportMessage(RebusMessage.Headers, body);
        }

        private string TryGetHeaderValue(string key)
        {
            if (RebusMessage.Headers.ContainsKey(key))
                return RebusMessage.Headers[key];
            return string.Empty;
        }

        private string GetUrnTypeName()
        {
            var namespaceParts = new List<string>();

            var messageType = TryGetHeaderValue(Rebus.Messages.Headers.Type);
            if (!string.IsNullOrWhiteSpace(messageType))
            {
                var fullName = messageType.Contains(",") ? messageType.Split(',')[0] : messageType;
                namespaceParts = fullName.Contains(".") ? fullName.Split('.').ToList() : new List<string> {fullName};
            }

            if (namespaceParts.Count == 0)
            {
                _logger.Error($"Cannot convert to masstransit message; type header is: '{messageType}'");
                return String.Empty;
            }

            var objectName = namespaceParts.Last();

            if (namespaceParts.Count == 1)
                return $"urn:message:{objectName}";

            namespaceParts.Remove(objectName);
            var rootName = string.Join('.', namespaceParts);
            return $"urn:message:{rootName}:{objectName}";
        }
    }
}