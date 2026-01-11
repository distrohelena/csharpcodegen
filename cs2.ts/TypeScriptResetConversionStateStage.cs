using cs2.core.Pipeline;

namespace cs2.ts {
    /// <summary>
    /// Conversion stage that resets per-project state while preserving accumulated program classes.
    /// </summary>
    internal sealed class TypeScriptResetConversionStateStage : IConversionStage {
        /// <summary>
        /// Resets the conversion context without clearing previously converted classes.
        /// </summary>
        /// <param name="session">The conversion session being processed.</param>
        public void Execute(ConversionSession session) {
            if (session == null) {
                return;
            }

            session.Context.Reset(true);
        }
    }
}
