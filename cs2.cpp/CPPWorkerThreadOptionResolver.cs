using System.Globalization;

namespace cs2.cpp {
    /// <summary>
    /// Resolves the dedicated worker thread count for parallel converter phases from generic platform options.
    /// </summary>
    public static class CPPWorkerThreadOptionResolver {
        /// <summary>
        /// Returns the configured worker count, defaulting to the logical processor count when the option is absent.
        /// </summary>
        /// <param name="options">Active conversion options.</param>
        /// <returns>A positive worker thread count.</returns>
        /// <remarks>
        /// There is no upper bound on the configured value: any positive integer is accepted, and the worker pool caps the threads it actually starts at the number of work items, so a count above the item count costs nothing.
        /// </remarks>
        public static int Resolve(CPPConversionOptions options) {
            if (options == null) {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.PlatformOptionValues == null ||
                !options.PlatformOptionValues.TryGetValue(CPPCodegenOptionNames.WorkerThreads, out string rawValue)) {
                return Environment.ProcessorCount;
            }

            if (!int.TryParse(rawValue?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int workerCount) || workerCount < 1) {
                throw new InvalidOperationException($"Option '{CPPCodegenOptionNames.WorkerThreads}' must be a positive integer; got '{rawValue}'.");
            }

            return workerCount;
        }
    }
}
