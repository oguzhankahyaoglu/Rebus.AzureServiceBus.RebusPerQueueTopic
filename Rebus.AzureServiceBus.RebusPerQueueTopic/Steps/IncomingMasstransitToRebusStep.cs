using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rebus.AzureServiceBus.RebusPerQueueTopic.Internals;
using Rebus.Logging;
using Rebus.Messages;
using Rebus.Pipeline;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    [StepDocumentation(@"Tries to handle Masstransit messages by reading contents and assigning messageId headers")]
    public class IncomingMasstransitToRebusStep : IIncomingStep
    {
        private readonly ILog _log;

        public IncomingMasstransitToRebusStep(ILog log)
        {
            _log = log;
        }

        public async Task Process(IncomingStepContext context,
            Func<Task> next)
        {
            var transportMessage = context.Load<TransportMessage>();
            //mesajda rebus headerları (messageId gibi) yoksa bu zaten MT'den geliyordur varsayımı ypaıyoruz
            //o zaman da içindeki body'den gereken headerları ayıklayıp
            //sonra message:{} içindeki jsonu alıp, ası mesajmış gibi replace ediyoruz
            if (!transportMessage.Headers.TryGetValue(Headers.MessageId, out _))
            {
                var (parsedMessageId, parsedType) = MasstransitHelpers.SetMessageIdFromMasstransitMessage(
                    transportMessage.Body.ToArray(), _log);
                transportMessage.Headers[Headers.MessageId] = parsedMessageId;
                transportMessage.Headers[Headers.ContentType] = "application/json;charset=utf-8";

                var foundType = MessageTypeFinder.FindType(parsedType);
                transportMessage.Headers[Headers.Type] = foundType?.AssemblyQualifiedName;

                //mt body'sini rebus body'e çevirmek:
                transportMessage = ConvertMtMessageToRebusMessage(transportMessage, true);
                context.Save(transportMessage);
            }
            else
            {
                //bu mesajı rebus bir projeden QueueOneWayMt gibi göndermiş olabiliriz,
                //o zaman yine message:{} ve messagetype:[] barındıracak
                //bu tipteki durumları da handle edebilelim:
                transportMessage = ConvertMtMessageToRebusMessage(transportMessage, false);
                context.Save(transportMessage);
            }


            await next();
        }

        private TransportMessage ConvertMtMessageToRebusMessage(TransportMessage transportMessage,
            bool errorLog)
        {
            var serializer = new JsonSerializer();
            using (var messageStream = new MemoryStream(transportMessage.Body))
            using (var sr = new StreamReader(messageStream))
            using (var reader = new JsonTextReader(sr))
            {
                var deserializedBody = serializer.Deserialize<JObject>(reader);
                //mt mesajı olup olmadığını anlamak için :
                /*
                 *
    "messageType": [
		"urn:message:TeiasOsosApi.Integration.Events:FormulaCoalBusbarIndexRunCommand"
	],
	"message": {
		"id": "4c3a07f7-7b87-4148-b2b5-90122748b0d1",
		"source": "JobsBackfillAppService",
	}
                 */
                if (deserializedBody.Property("messageType") == null
                    && deserializedBody.Property("message") == null)
                {
                    //bu bir rebus mesajdır, geçebiliriz
                    if (errorLog)
                        _log.Error("message does not contain 'message' or 'messageType', assuming Rebus message");
                    else
                        _log.Debug("message does not contain 'message' or 'messageType', assuming Rebus message");
                    return transportMessage;
                }

                var mtBody = deserializedBody.Property("message");
                if (mtBody != null)
                {
                    var envelopeJson = JsonConvert.SerializeObject(mtBody.Value, Formatting.None, AzureRebusCommon.NewtonsoftJsonSettings);
                    var body = Encoding.UTF8.GetBytes(envelopeJson);
                    var result = new TransportMessage(transportMessage.Headers, body);
                    return result;
                }

                if (errorLog)
                    _log.Error("message does not contain 'message' or 'messageType', assuming Rebus message #2");
                else
                    _log.Debug("message does not contain 'message' or 'messageType', assuming Rebus message #2");
                return transportMessage;
            }
        }
    }
}