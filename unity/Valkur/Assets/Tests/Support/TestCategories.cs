namespace Valkur.Tests.Support
{
    /// <summary>
    /// The closed vocabulary for <c>[Category]</c> in Valkur's test suites.
    ///
    /// <para><b>Why a closed list.</b> A category is only useful as a FILTER, and a filter only
    /// works if everybody spells it the same way. Before this list the whole suite carried twelve
    /// categories, all <c>"DataIntegrity"</c>, so "run the guards" or "skip the slow ones" had no
    /// answer. <c>TestLayoutConventionTests</c> fails any <c>[Category("…")]</c> whose value is not
    /// one of these constants.</para>
    ///
    /// <para><b>How to run one.</b> MCP: <c>run_tests(mode="EditMode", category_names=["Guard"])</c>.
    /// The Test Runner window has a category dropdown. From the CLI: <c>-testCategory "Guard"</c>;
    /// a leading <c>!</c> excludes (<c>-testCategory "!Slow"</c>).</para>
    /// </summary>
    public static class TestCategories
    {
        /// <summary>
        /// A rule about the whole project rather than one system: source scans (no raw input reads,
        /// no bindings built in code, statics reset, brace balance), asset conventions, TagManager.
        /// Everything under <c>EditMode/Project/</c> carries it. Run these after any change that
        /// touches many files; they are the ones that fail for code you did not write.
        /// </summary>
        public const string Guard = "Guard";

        /// <summary>
        /// Reads data the game SHIPS (catalogues, <c>.asset</c> files, StreamingAssets JSON) and
        /// asserts it is coherent. Red here after a data edit means the data, not the code. Remember
        /// <c>refresh_unity(scope="all")</c> first: an edited <c>.asset</c> is not reimported by a
        /// scripts-only refresh and the test would measure the stale in-memory copy.
        /// </summary>
        public const string ShippedData = "ShippedData";

        /// <summary>
        /// Exercises several systems composed together (a save written and loaded back, a world
        /// streamed end to end, an editor driving the runtime). Slower and broader than a unit
        /// test; the first place to look when two halves are each green and the game is not.
        /// </summary>
        public const string Integration = "Integration";

        /// <summary>
        /// Measured, not guessed: a test that took 0.5 s or more, or a fixture whose tests took 2 s
        /// or more in total, on the reference run recorded by <c>TestRunRecorder</c>
        /// (<c>Library/ValkurTestResults/</c>). Exclude with <c>!Slow</c> for a quick loop.
        /// Re-derive after a big optimisation rather than hand-editing the attributes.
        /// </summary>
        public const string Slow = "Slow";

        /// <summary>Every value above, for the convention test.</summary>
        public static readonly string[] All = { Guard, ShippedData, Integration, Slow };
    }
}
