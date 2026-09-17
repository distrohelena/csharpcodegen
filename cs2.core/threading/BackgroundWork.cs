using System.Runtime.ExceptionServices;

namespace cs2.core.Threading {
    /// <summary>
    /// Runs one action on a dedicated named thread and hands its failure back to whoever waits for it.
    /// </summary>
    public sealed class BackgroundWork {
        /// <summary>
        /// Thread executing the body.
        /// </summary>
        readonly Thread Worker;

        /// <summary>
        /// Exception captured from the body, published to the waiting thread by the join.
        /// </summary>
        Exception Failure;

        /// <summary>
        /// Starts the body immediately on a background thread with the supplied name.
        /// </summary>
        /// <param name="name">Thread name shown in debuggers and dumps.</param>
        /// <param name="body">Work to execute exactly once.</param>
        public BackgroundWork(string name, Action body) {
            if (string.IsNullOrWhiteSpace(name)) {
                throw new ArgumentException("Background work requires a thread name.", nameof(name));
            }
            if (body == null) {
                throw new ArgumentNullException(nameof(body));
            }

            Worker = new Thread(() => Execute(body)) {
                Name = name,
                IsBackground = true
            };
            Worker.Start();
        }

        /// <summary>
        /// Gets whether the body has finished, successfully or not.
        /// </summary>
        public bool IsCompleted => !Worker.IsAlive;

        /// <summary>
        /// Blocks until the body finishes and rethrows its failure with the original stack trace.
        /// </summary>
        public void Wait() {
            Worker.Join();
            if (Failure != null) {
                ExceptionDispatchInfo.Capture(Failure).Throw();
            }
        }

        /// <summary>
        /// Blocks until the body finishes without surfacing its failure.
        /// </summary>
        public void Join() {
            Worker.Join();
        }

        /// <summary>
        /// Runs the body and records any exception for the waiting thread.
        /// </summary>
        /// <param name="body">Work to execute.</param>
        void Execute(Action body) {
            try {
                body();
            } catch (Exception exception) {
                Failure = exception;
            }
        }
    }
}
