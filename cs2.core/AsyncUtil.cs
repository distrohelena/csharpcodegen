using System;
using System.Threading.Tasks;

namespace cs2.core {
    /// <summary>
    /// Provides synchronous wrappers for task-based APIs used by the conversion pipeline.
    /// </summary>
    public static class AsyncUtil {
        /// <summary>
        /// Executes an asynchronous operation and returns its result without exposing task-based call sites to the rest of the converter.
        /// </summary>
        /// <typeparam name="TResult">The result type returned by the asynchronous operation.</typeparam>
        /// <param name="taskFactory">Factory that creates the task to execute.</param>
        /// <returns>The result produced by the completed task.</returns>
        public static TResult RunSync<TResult>(Func<Task<TResult>> taskFactory) {
            if (taskFactory == null) {
                throw new ArgumentNullException(nameof(taskFactory));
            }

            return taskFactory().GetAwaiter().GetResult();
        }
    }
}
