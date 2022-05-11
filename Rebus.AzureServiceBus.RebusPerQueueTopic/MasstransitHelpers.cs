using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Rebus.Logging;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic
{
    public static class MasstransitHelpers
    {
        /// <summary>
        /// Masstransit'ten gelen mesajın id'sini alabilmek için eklendi. Header'da bekliyor Rebus mesajın id'sini
        /// </summary>
        /// <param name="messageBytes"></param>
        /// <param name="log"></param>
        /// <param name="message"></param>
        public static (string, string) SetMessageIdFromMasstransitMessage(byte[] messageBytes,
            ILog log)
        {
            var serializer = new JsonSerializer();
            using (var messageStream = new MemoryStream(messageBytes))
            using (var sr = new StreamReader(messageStream))
            using (var reader = new JsonTextReader(sr))
            {
                var deserializedBody = serializer.Deserialize<JObject>(reader);
                // var deserializedId = deserializedBody.Property("messageId")?.Value;
                var deserializedConversationId = deserializedBody.Property("conversationId")?.Value;
                var result = deserializedConversationId?.ToString();
                //mt conversationId içerisnde message id gönderiyordu
                //ytbs robottan mt mesajı attığımızda, body'de id geliyor
                if (string.IsNullOrEmpty(result))
                {
                    var mtBody = deserializedBody.Property("message");
                    if (mtBody != null)
                    {
                        result = mtBody.Value["id"]?.ToString();
                        if (!string.IsNullOrEmpty(result))
                        {
                            log.Warn("{Name}Cannot get 'conversationId' from mt message, using 'message.id' as id", 
                                nameof(SetMessageIdFromMasstransitMessage));
                        }
                        else
                        {
                            log.Error("{Name}Cannot get 'conversationId' from mt message, using 'message.id' as id {message}", 
                                nameof(SetMessageIdFromMasstransitMessage), deserializedBody);
                        }
                    }
                    else
                    {
                        log.Error("{Name}Cannot get 'conversationId' from mt message, body is null how? {message}", 
                            nameof(SetMessageIdFromMasstransitMessage), deserializedBody);
                    }
                }

                var messageType = deserializedBody.GetValue("messageType")?.Last?.ToString();
                //urn:message:TeiasOsosApi.Integration.Events:QueueTestMessage
                //masstransit mesaj formatı
                if (!string.IsNullOrEmpty(messageType))
                    messageType = messageType.Split(':', StringSplitOptions.RemoveEmptyEntries)
                        .LastOrDefault();
                return (result, messageType);
            }
        }

    }
}