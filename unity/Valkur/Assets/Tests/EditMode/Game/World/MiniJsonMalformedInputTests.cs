using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using NUnit.Framework;
using Valkur.Gameplay.World;

namespace Valkur.Tests.EditMode.Game.World
{
    /// <summary>
    /// Malformed JSON must FAIL, and it must fail FAST.
    ///
    /// <para>Measured before this fixture existed: <c>MiniJsonRuntime.Deserialize("{ this is
    /// not valid json")</c> ran for 20.3 seconds and then threw OutOfMemory. The parser's
    /// <c>NextChar</c> cast <c>StringReader.Read()</c>'s -1 to <c>'\uFFFF'</c>, which is
    /// neither a quote nor a backslash, so an unterminated string appended it to a
    /// StringBuilder forever; and <c>ParseObject</c> / <c>ParseArray</c> looped on
    /// <c>while (true)</c> with no guarantee of progress, so <c>[1,</c> was
    /// <c>list.Add("")</c> until memory ran out. Eighteen loaders parse hand-editable
    /// <c>StreamingAssets</c> files through this class — maps, buildings, zones, spawners,
    /// particles, FSM sets — and any of them corrupted by a stray keystroke froze the game
    /// that way. The only place it showed was one FSM test that quietly took 20 s of every
    /// suite run.</para>
    ///
    /// <para>The time bound is the hang guard. 250 ms is fifty times what any of these inputs
    /// costs and a hundredth of what the bug cost, so it separates the two states without
    /// being a flake on a busy machine.</para>
    /// </summary>
    [TestFixture]
    public class MiniJsonMalformedInputTests
    {
        private const int HANG_GUARD_MS = 250;

        /// <summary>Structurally broken: no correct parse exists, so the result must be null.</summary>
        private static readonly string[] Malformed =
        {
            "{ this is not valid json",     // the input that cost 20 s
            "{",
            "[",
            "{\"a\"",
            "{\"a\":",
            "{\"a\":}",
            "{\"a\" 1}",                    // key without colon
            "{ a: 1 }",                     // unquoted key
            "[1,",
            "[1 2",
            "\"unterminated",
            "{\"a\":\"b",
            "\"\\u12\"",                    // truncated unicode escape
            "\"\\u12",
            "   ",
            "{\"a\":[1,2,{\"b\":",          // nested, cut mid-way
        };

        private static (object result, long ms) Time(Func<object> parse)
        {
            var sw = Stopwatch.StartNew();
            object r = parse();
            sw.Stop();
            return (r, sw.ElapsedMilliseconds);
        }

        [Test]
        public void Runtime_MalformedInput_ReturnsNull_AndReturnsQuickly()
        {
            var failures = new List<string>();
            foreach (var input in Malformed)
            {
                var (result, ms) = Time(() => MiniJsonRuntime.Deserialize(input));
                if (result != null)
                    failures.Add($"{Show(input)} parsed to {result.GetType().Name} instead of null");
                if (ms > HANG_GUARD_MS)
                    failures.Add($"{Show(input)} took {ms} ms — the parser is looping again");
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        /// <summary>
        /// The one that was actually measured, kept as its own test so a regression names
        /// the exact input in the failure message.
        /// </summary>
        [Test]
        public void Runtime_TheInputThatCost20Seconds_IsRefusedInMilliseconds()
        {
            var (result, ms) = Time(() => MiniJsonRuntime.Deserialize("{ this is not valid json"));
            Assert.IsNull(result);
            Assert.Less(ms, HANG_GUARD_MS, "This input used to run for 20 s and die of OutOfMemory.");
        }

        /// <summary>
        /// The fix must not have made the parser stricter about VALID JSON. Every shape the
        /// shipped files use, round-tripped.
        /// </summary>
        [Test]
        public void Runtime_ValidJson_StillParses()
        {
            var obj = MiniJsonRuntime.Deserialize(
                "{\"id\":\"x\",\"n\":-12,\"f\":1.5e2,\"t\":true,\"z\":null," +
                "\"s\":\"a\\\"b\\\\c\\n\\u0041\",\"list\":[1,\"two\",[3],{}],\"o\":{\"k\":[]}}")
                as Dictionary<string, object>;

            Assert.IsNotNull(obj);
            Assert.AreEqual("x", obj["id"]);
            Assert.AreEqual(-12.0, Convert.ToDouble(obj["n"]), 1e-9);
            // 1.5e2 arrives as long 150: long.TryParse with NumberStyles.Any accepts an
            // exponent. Pre-existing, and harmless — every consumer goes through
            // Convert.ToSingle/ToDouble on numeric fields.
            Assert.AreEqual(150.0, Convert.ToDouble(obj["f"]), 1e-9);
            Assert.AreEqual(true, obj["t"]);
            Assert.IsNull(obj["z"]);
            Assert.AreEqual("a\"b\\c\nA", obj["s"]);
            var list = obj["list"] as List<object>;
            Assert.AreEqual(4, list.Count);
            Assert.AreEqual(1L, list[0]);
            Assert.AreEqual("two", list[1]);
            Assert.AreEqual(3L, ((List<object>)list[2])[0]);
            Assert.IsInstanceOf<Dictionary<string, object>>(list[3]);
            Assert.AreEqual(0, ((List<object>)((Dictionary<string, object>)obj["o"])["k"]).Count);
        }

        /// <summary>
        /// Leniencies the old parser had and shipped files may lean on: trailing garbage after
        /// a complete value, a bare word as a value, and a trailing comma. None of these is
        /// JSON, but none of them can loop either, so they stay accepted rather than turned
        /// into a new way for a months-old file to stop loading.
        /// </summary>
        [Test]
        public void Runtime_LenientButTerminatingShapes_StillParse()
        {
            Assert.IsNotNull(MiniJsonRuntime.Deserialize("{\"a\":1} trailing"), "trailing garbage");
            Assert.AreEqual("word", MiniJsonRuntime.Deserialize("word"), "bare word");
            var trailingComma = MiniJsonRuntime.Deserialize("[1,2,]") as List<object>;
            Assert.IsNotNull(trailingComma, "trailing comma in array");
            Assert.AreEqual(2, trailingComma.Count);
            var trailingCommaObj = MiniJsonRuntime.Deserialize("{\"a\":1,}") as Dictionary<string, object>;
            Assert.IsNotNull(trailingCommaObj, "trailing comma in object");
            // A lone comma is the same leniency from the other side: skipped, never looped on.
            var loneComma = MiniJsonRuntime.Deserialize("[,]") as List<object>;
            Assert.IsNotNull(loneComma, "lone comma in array");
            Assert.AreEqual(0, loneComma.Count);
        }

        /// <summary>
        /// The Editor-assembly twin (<c>Valkur.Editor.MiniJson</c>, internal) is an
        /// index-based rewrite with the same failure family: a value its number parser cannot
        /// consume made no progress and <c>[x</c> was <c>list.Add(0)</c> forever. It feeds
        /// the persona and FSM-seed importers, so it is reached through reflection rather
        /// than by widening the Editor assembly's internals for a test.
        /// </summary>
        [Test]
        public void Editor_MalformedInput_Terminates()
        {
            var type = Type.GetType("Valkur.Editor.MiniJson, Valkur.Editor");
            Assert.IsNotNull(type, "Valkur.Editor.MiniJson not found — was it renamed?");
            var deserialize = type.GetMethod("Deserialize",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null, new[] { typeof(string) }, null);
            Assert.IsNotNull(deserialize, "MiniJson.Deserialize(string) not found.");

            var failures = new List<string>();
            foreach (var input in Malformed)
            {
                long ms;
                try
                {
                    ms = Time(() => deserialize.Invoke(null, new object[] { input })).ms;
                }
                catch (TargetInvocationException e)
                {
                    // The Editor copy is lenient and may throw on some garbage; a throw is a
                    // termination, which is all this test asks of it. Only a hang is a bug.
                    failures.Add($"{Show(input)} threw {e.InnerException?.GetType().Name}");
                    continue;
                }
                if (ms > HANG_GUARD_MS)
                    failures.Add($"{Show(input)} took {ms} ms in the Editor parser");
            }

            // Throws are reported but do not fail: they are the old behaviour and terminate.
            var hangs = failures.FindAll(f => f.Contains(" took "));
            Assert.IsEmpty(hangs, string.Join("\n", failures));
        }

        private static string Show(string s) => "'" + s.Replace("\n", "\\n") + "'";
    }
}
