using System;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Rebus.Extensions;
using Rebus.Messages;
using Rebus.Serialization;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Serializers
{
    public class BrotliJsonSerializer : ISerializer
    {
        /// <summary>
        /// Proper content type when a message has been serialized with this serializer (or another compatible JSON serializer) and it uses the standard UTF8 encoding
        /// </summary>
        public const string JsonUtf8ContentType = "application/json;charset=utf-8";

        /// <summary>Contents type when the content is JSON</summary>
        public const string JsonContentType = "application/json";

        private static readonly JsonSerializerSettings DefaultSettings = new()
        {
            TypeNameHandling = TypeNameHandling.All,
            Formatting = Formatting.None
        };

        private static readonly Encoding DefaultEncoding = Encoding.UTF8;
        private readonly JsonSerializerSettings _settings;
        private readonly Encoding _encoding;
        private readonly IMessageTypeNameConvention _messageTypeNameConvention;
        private readonly string _encodingHeaderValue;

        public BrotliJsonSerializer(
            IMessageTypeNameConvention messageTypeNameConvention,
            JsonSerializerSettings jsonSerializerSettings = null,
            Encoding encoding = null)
        {
            _messageTypeNameConvention = messageTypeNameConvention ?? throw new ArgumentNullException(nameof(messageTypeNameConvention));
            _settings = jsonSerializerSettings ?? DefaultSettings;
            _encoding = encoding ?? DefaultEncoding;
            _encodingHeaderValue = "application/json;charset=" + _encoding.HeaderName;
        }

        /// <summary>
        /// Serializes the given <see cref="T:Rebus.Messages.Message" /> into a <see cref="T:Rebus.Messages.TransportMessage" />
        /// </summary>
        public async Task<TransportMessage> Serialize(Message message)
        {
            var serializedJson = JsonConvert.SerializeObject(message.Body, _settings);
            var bytesOriginal = _encoding.GetBytes(serializedJson);
            
            var compressed = await serializedJson.ToBrotliAsync(CompressionLevel.Optimal);
            var bytes = _encoding.GetBytes(compressed.Result.Value);
            var headers = message.Headers.Clone();
            headers["rbs2-content-type"] = _encodingHeaderValue;
            headers["rbs2-compressed"] = "brotli";
            if (!headers.ContainsKey("rbs2-msg-type"))
                headers["rbs2-msg-type"] = _messageTypeNameConvention.GetTypeName(message.Body.GetType());
            return new TransportMessage(headers, bytes);
        }

        /// <summary>
        /// Deserializes the given <see cref="T:Rebus.Messages.TransportMessage" /> back into a <see cref="T:Rebus.Messages.Message" />
        /// </summary>
        public async Task<Message> Deserialize(TransportMessage transportMessage)
        {
            var contentType = transportMessage.Headers.GetValue("rbs2-content-type");
            if (contentType.Equals("application/json;charset=utf-8", StringComparison.OrdinalIgnoreCase))
                return await GetMessage(transportMessage, _encoding);
            var bodyEncoding = contentType.StartsWith("application/json")
                ? GetEncoding(contentType)
                : throw new FormatException("Unknown content type: '" + contentType +
                                            "' - must be 'application/json' (e.g. 'application/json;charset=utf-8') for the JSON serialier to work");
            return await GetMessage(transportMessage, bodyEncoding);
        }

        private Encoding GetEncoding(string contentType)
        {
            var strArray = contentType.Split(';')
                .Select((Func<string, string[]>)(token => token.Split('=')))
                .Where((Func<string[], bool>)(tokens => tokens.Length == 2))
                .FirstOrDefault((Func<string[], bool>)(tokens => tokens[0] == "charset"));
            if (strArray == null)
                return _encoding;
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

        private async Task<Message> GetMessage(TransportMessage transportMessage, Encoding bodyEncoding)
        {
            var messageBodyStr = bodyEncoding.GetString(transportMessage.Body);
            if (transportMessage.Headers.GetValue("rbs2-compressed") == "brotli")
            {
                messageBodyStr = await CompressionHelper.FromBrotliAsync(messageBodyStr);
            }

            var deserializedBody = Deserialize(messageBodyStr, GetTypeOrNull(transportMessage));
            return new Message(transportMessage.Headers.Clone(), deserializedBody);
        }

        private Type GetTypeOrNull(TransportMessage transportMessage)
        {
            if (!transportMessage.Headers.TryGetValue("rbs2-msg-type", out var name))
                return null;
            return _messageTypeNameConvention.GetType(name) ?? throw new FormatException("Could not get .NET type named '" + name + "'");
        }

        private object Deserialize(string bodyString, Type type)
        {
            try
            {
                return type == null
                    ? JsonConvert.DeserializeObject(bodyString, _settings)
                    : JsonConvert.DeserializeObject(bodyString, type, _settings);
            }
            catch (Exception ex)
            {
                if (bodyString.Length > 32768)
                    throw new FormatException(
                        string.Format("Could not deserialize JSON text (original length: {0}): '{1}'", bodyString.Length,
                            Limit(bodyString, 5000)), ex);
                throw new FormatException("Could not deserialize JSON text: '" + bodyString + "'", ex);
            }
        }

        private static string Limit(string bodyString, int maxLength) => bodyString.Substring(0, maxLength) + " (......)";
    }
}