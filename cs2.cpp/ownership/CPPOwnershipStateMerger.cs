namespace cs2.cpp;

/// <summary>
/// Conservatively merges local native ownership states arriving at one control-flow join.
/// </summary>
public sealed class CPPOwnershipStateMerger {
    /// <summary>
    /// Merges predecessor states without guessing across ownership or lifecycle disagreement.
    /// </summary>
    /// <param name="states">States from every reachable predecessor.</param>
    /// <param name="permitsUninitializedOwned">Whether a false-initialized ownership guard safely represents null plus live-owned paths.</param>
    /// <returns>The common state, a guarded live-owned state, or an ambiguous unknown state.</returns>
    public CPPLocalOwnershipState Merge(
        IReadOnlyList<CPPLocalOwnershipState> states,
        bool permitsUninitializedOwned) {
        if (states == null) {
            throw new ArgumentNullException(nameof(states));
        }
        if (states.Count == 0) {
            return CPPLocalOwnershipState.CreateUninitialized();
        }

        CPPLocalOwnershipState firstState = states[0];
        if (states.All(firstState.SemanticallyEquals)) {
            return firstState;
        }

        bool containsOnlyUninitializedOrLiveOwned = states.All(state =>
            !state.IsInitialized ||
            (state.Ownership == CPPOwnershipKind.Owned && state.Lifecycle == CPPOwnershipLifecycle.Live));
        bool containsLiveOwned = states.Any(state =>
            state.IsInitialized &&
            state.Ownership == CPPOwnershipKind.Owned &&
            state.Lifecycle == CPPOwnershipLifecycle.Live);
        if (permitsUninitializedOwned && containsOnlyUninitializedOrLiveOwned && containsLiveOwned) {
            return new CPPLocalOwnershipState(
                CPPOwnershipKind.Owned,
                CPPOwnershipLifecycle.Live,
                true);
        }

        bool containsOnlyUninitializedOrBorrowed = states.All(state =>
            !state.IsInitialized || state.Ownership == CPPOwnershipKind.Borrowed);
        bool containsBorrowed = states.Any(state => state.IsInitialized && state.Ownership == CPPOwnershipKind.Borrowed);
        if (containsOnlyUninitializedOrBorrowed && containsBorrowed) {
            return new CPPLocalOwnershipState(
                CPPOwnershipKind.Borrowed,
                CPPOwnershipLifecycle.Live,
                true);
        }

        return new CPPLocalOwnershipState(
            CPPOwnershipKind.Unknown,
            CPPOwnershipLifecycle.Live,
            true);
    }
}
