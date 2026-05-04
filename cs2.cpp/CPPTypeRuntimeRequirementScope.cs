namespace cs2.cpp {
    /// <summary>
    /// Tracks the runtime helpers used while emitting a single generated type.
    /// </summary>
    public sealed class CPPTypeRuntimeRequirementScope {
        readonly HashSet<string> runtimeRequirements = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Registers a runtime helper name for the current emitted type.
        /// </summary>
        /// <param name="requirementName">Stable runtime helper name to record.</param>
        public void Register(string requirementName) {
            if (string.IsNullOrWhiteSpace(requirementName)) {
                throw new ArgumentException("Runtime requirement name must not be empty.", nameof(requirementName));
            }

            runtimeRequirements.Add(requirementName);
        }

        /// <summary>
        /// Returns the runtime helper names registered for the current emitted type.
        /// </summary>
        /// <returns>Stable runtime helper names for the emitted type.</returns>
        public IReadOnlyCollection<string> GetRegisteredRequirements() {
            return runtimeRequirements.ToArray();
        }
    }
}
