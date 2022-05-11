using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using Rebus.AzureServiceBus.RebusPerQueueTopic.AzureServiceBus;

namespace Rebus.AzureServiceBus.RebusPerQueueTopic.Internals
{
    static class AsyncHelpers
    {
        public static TResult ReturnSync<TResult>(Func<Task<TResult>> task)
        {
            var result = default(TResult);
            RunSync(async () => result = await task().ConfigureAwait(false));
            return result;
        }

        /// <summary>
        /// Executes a task synchronously on the calling thread by installing a temporary synchronization context that queues continuations
        ///  </summary>
        public static void RunSync(Func<Task> task)
        {
            var currentContext = SynchronizationContext.Current;
            var customContext = new CustomSynchronizationContext(task);

            try
            {
                SynchronizationContext.SetSynchronizationContext(customContext);

                customContext.Run();
            }
            finally
            {
                SynchronizationContext.SetSynchronizationContext(currentContext);
            }
        }

        /// <summary>
        /// Synchronization context that can be "pumped" in order to have it execute continuations posted back to it
        /// </summary>
        class CustomSynchronizationContext : SynchronizationContext
        {
            readonly ConcurrentQueue<Tuple<SendOrPostCallback, object>> _items = new ConcurrentQueue<Tuple<SendOrPostCallback, object>>();
            readonly AutoResetEvent _workItemsWaiting = new AutoResetEvent(false);
            readonly Func<Task> _task;

            ExceptionDispatchInfo _caughtException;

            bool _done;

            public CustomSynchronizationContext(Func<Task> task)
            {
                _task = task ?? throw new ArgumentNullException(nameof(task), "Please remember to pass a Task to be executed");
            }

            public override void Post(SendOrPostCallback function, object state)
            {
                _items.Enqueue(Tuple.Create(function, state));
                _workItemsWaiting.Set();
            }

            /// <summary>
            /// Enqueues the function to be executed and executes all resulting continuations until it is completely done
            /// </summary>
            public void Run()
            {
                Post(async _ =>
                    {
                        try
                        {
                            await _task().ConfigureAwait(false);
                        }
                        catch (Exception exception)
                        {
                            _caughtException = ExceptionDispatchInfo.Capture(exception);
                            throw;
                        }
                        finally
                        {
                            Post(state => _done = true, null);
                        }
                    }, null);

                while (!_done)
                {
                    if (_items.TryDequeue(out var task))
                    {
                        task.Item1(task.Item2);

                        if (_caughtException == null) continue;

                        _caughtException.Throw();
                    }
                    else
                    {
                        _workItemsWaiting.WaitOne();
                    }
                }
            }

            public override void Send(SendOrPostCallback d, object state)
            {
                throw new NotSupportedException("Cannot send to same thread");
            }

            public override SynchronizationContext CreateCopy()
            {
                return this;
            }
        }

        public static async Task CancelAfterAsync(
            Func<CancellationToken, Task> startTask,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            using (var timeoutCancellation = new CancellationTokenSource())
            using (var combinedCancellation = CancellationTokenSource
                .CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token))
            {
                var originalTask = startTask(combinedCancellation.Token);
                var delayTask = Task.Delay(timeout, timeoutCancellation.Token);
                var completedTask = await Task.WhenAny(originalTask, delayTask);
                // Cancel timeout to stop either task:
                // - Either the original task completed, so we need to cancel the delay task.
                // - Or the timeout expired, so we need to cancel the original task.
                // Canceling will not affect a task, that is already completed.
                timeoutCancellation.Cancel();
                if (completedTask == originalTask)
                {
                    // original task completed
                    await originalTask;
                }
                else
                {
                    // timeout
                    throw new RebusExecutionTimeoutException("Execution failed after " + timeout);
                }
            }
        }
    }
}