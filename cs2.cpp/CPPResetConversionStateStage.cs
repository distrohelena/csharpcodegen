using cs2.core.Pipeline;

namespace cs2.cpp {
    /// <summary>
    /// Resets backend-specific run state before a C++ conversion pipeline executes.
    /// </summary>
    internal sealed class CPPResetConversionStateStage : IConversionStage {
        /// <summary>
        /// Holds the converter whose per-run state is being managed.
        /// </summary>
        readonly CPPCodeConverter Owner;

        /// <summary>
        /// Initializes the reset stage for the owning converter.
        /// </summary>
        /// <param name="owner">Converter whose state is reset between runs.</param>
        public CPPResetConversionStateStage(CPPCodeConverter owner) {
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        /// <summary>
        /// Clears transient conversion state while preserving runtime metadata loaded into the native class catalog.
        /// </summary>
        /// <param name="session">The active conversion session.</param>
        public void Execute(ConversionSession session) {
            if (session == null) {
                return;
            }

            session.Context.Reset();
            Owner.ResetRunState();
        }
    }
}
