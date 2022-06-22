using System;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigMasstransit;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueue;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigQueueOneWay;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopicOneWay;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ConfigTopics;
using Rebus.AzureServiceBus.RebusPerQueueTopic.ErrorHandling;
using Rebus.Config;
using Rebus.Logging;
using Rebus.Pipeline;
using Rebus.Pipeline.Receive;
using Rebus.Pipeline.Send;
using Rebus.Retry.Simple;
using Rebus.Serialization;
using Rebus.Transport;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Steps
{
    internal static class AzureServiceBusStepRegistrar
    {
        public static void Register<TTransport>(StandardConfigurer<ITransport> configurer,
            bool compress) where TTransport : ITransport
        {
            IncomingBrotliCompressionStep(configurer);
            IncomingMasstransitToRebusStep(configurer);
            IncomingMessageTypeHeaderAdjusterStep(configurer);
            HandleDeferredMessagesStep(configurer);

             switch (typeof(TTransport).Name)
            {
                case nameof(AzureServiceBusQueueOneWayToMasstransitTransport):
                case nameof(AzureServiceBusTopicOneWayToMasstransitTransport):
                    OutgoingRebusToMasstransitStep(configurer);
                    break;
                case nameof(AzureServiceBusQueueTransport):
                case nameof(AzureServiceBusTopicTransport):
                    IncomingMessageExecutionTimeoutWrappingStep(configurer);
                    break;
                case nameof(AzureServiceBusQueueOneWayTransport):
                case nameof(AzureServiceBusTopicOneWayTransport):
                    break;
                default:
                    throw new NotImplementedException("Any other transport step registration is ignored, check registrations");
            }
             
             if (compress)
                 OutgoingBrotliCompressStep(configurer);
        }

        private static void IncomingMessageExecutionTimeoutWrappingStep(StandardConfigurer<ITransport> configurer)
        {
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();
                var settings = c.Get<RebusAzureServiceBusSettings>();
                var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                var logger = rebusLoggerFactory.GetLogger<OutgoingRebusToMasstransitStep>();
                var step = new IncomingMessageExecutionTimeoutWrappingStep(logger, settings);
                return new PipelineStepInjector(pipeline)
                    .OnReceive(step, PipelineRelativePosition.Before, typeof(DispatchIncomingMessageStep));
            });
        }

        private static void OutgoingRebusToMasstransitStep(StandardConfigurer<ITransport> configurer)
        {
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();
                var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                var logger = rebusLoggerFactory.GetLogger<OutgoingRebusToMasstransitStep>();
                var step = new OutgoingRebusToMasstransitStep(logger);
                return new PipelineStepInjector(pipeline)
                    .OnSend(step, PipelineRelativePosition.After, typeof(ValidateOutgoingMessageStep));
            });
        }

        private static void OutgoingBrotliCompressStep(StandardConfigurer<ITransport> configurer)
        {
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();
                var rebusLoggerFactory = c.Get<IRebusLoggerFactory>();
                var serializer = c.Get<ISerializer>();
                var logger = rebusLoggerFactory.GetLogger<OutgoingRebusToMasstransitStep>();
                var step = new OutgoingBrotliCompressStep(logger, serializer);
                return new PipelineStepInjector(pipeline)
                    .OnSend(step, PipelineRelativePosition.After, typeof(ValidateOutgoingMessageStep));
            });
        }

        private static void HandleDeferredMessagesStep(StandardConfigurer<ITransport> configurer)
        {
            //add AzureDeferredRetryStep 
            // configurer.OtherService<IPipeline>().Decorate(c =>
            // {
            //     var pipeline = c.Get<IPipeline>();
            //     var loggerFactory = c.Get<IRebusLoggerFactory>();
            //     var log = loggerFactory.GetLogger<AzureDeferredRetryStep>();
            //     var errorHandler = c.Get<IErrorHandler>();
            //     //TODO: log geldi mi? neden gelmedi?
            //     var step = new AzureDeferredRetryStep(log, retrySettings, errorHandler);
            //     return new PipelineStepInjector(pipeline)
            //         .OnReceive(step, PipelineRelativePosition.Before, typeof(FailFastStep));
            // });
            // remove deferred messages step
            //remove simple retry step, change it to AzureDeferredStrategyStep
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();

                return new PipelineStepRemover(pipeline)
                    .RemoveIncomingStep(s =>
                            s.GetType() == typeof(HandleDeferredMessagesStep)
                        // || s.GetType() == typeof(SimpleRetryStrategyStep)
                    );
            });

            // configurer.OtherService<IRetryStrategy>().Register(c =>
            // {
            //     var loggerFactory = c.Get<IRebusLoggerFactory>();
            //     var log = loggerFactory.GetLogger<AzureDeferredRetryStrategy>();
            //     var errorHandler = c.Get<IErrorHandler>();
            //     var errorTracker = c.Get<IErrorTracker>();
            //     //TODO: log geldi mi? neden gelmedi?
            //     return new AzureDeferredRetryStrategy(log,
            //         errorHandler, errorTracker, retrySettings);
            // });
        }

        private static void IncomingMessageTypeHeaderAdjusterStep(StandardConfigurer<ITransport> configurer)
        {
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();
                var loggerFactory = c.Get<IRebusLoggerFactory>();
                var log = loggerFactory.GetLogger<IncomingMessageTypeHeaderAdjusterStep>();
                var step = new IncomingMessageTypeHeaderAdjusterStep(log);
                return new PipelineStepInjector(pipeline)
                    .OnReceive(step, PipelineRelativePosition.Before, typeof(IncomingMasstransitToRebusStep));
            });
        }

        private static void IncomingMasstransitToRebusStep(StandardConfigurer<ITransport> configurer)
        {
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();
                var loggerFactory = c.Get<IRebusLoggerFactory>();
                var log = loggerFactory.GetLogger<IncomingMasstransitToRebusStep>();
                var step = new IncomingMasstransitToRebusStep(log);
                return new PipelineStepInjector(pipeline)
                    .OnReceive(step, PipelineRelativePosition.Before, typeof(SimpleRetryStrategyStep));
            });
        }

        private static void IncomingBrotliCompressionStep(StandardConfigurer<ITransport> configurer)
        {
            configurer.OtherService<IPipeline>().Decorate(c =>
            {
                var pipeline = c.Get<IPipeline>();
                var loggerFactory = c.Get<IRebusLoggerFactory>();
                var log = loggerFactory.GetLogger<IncomingBrotliCompressionStep>();
                var step = new IncomingBrotliCompressionStep(log);
                return new PipelineStepInjector(pipeline)
                    .OnReceive(step, PipelineRelativePosition.Before, typeof(SimpleRetryStrategyStep));
            });
        }
    }
}