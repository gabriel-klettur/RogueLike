using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;

namespace Valkur.Tests.EditMode.Game.Bootstrap
{
    /// <summary>
    /// A console command whose body RETURNS its answer must be registered through <c>Log(...)</c>.
    ///
    /// <para><b>Why this is a test and not a convention.</b> <c>ConsoleCommand.Handler</c> is an
    /// <c>Action&lt;string[]&gt;</c>, and C# happily compiles <c>args =&gt; CmdBoot(args)</c> against
    /// it by DISCARDING the string the method returns. The <c>boot</c> probe shipped that way: it
    /// printed nothing, ever, and since the handler also receives the command NAME in
    /// <c>args[0]</c> it read "boot" as its own subcommand, so <c>boot all</c> never worked either.
    /// Nothing failed — a command that answers nothing looks exactly like a command with nothing
    /// to say. Found while wiring <c>debughud</c>, which was written the same way first.</para>
    /// </summary>
    [TestFixture]
    public class DevConsoleHandlerContractTests
    {
        [Test]
        public void EveryCommandThatReturnsItsAnswer_IsLogged()
        {
            string dir = Path.Combine(Application.dataPath, "_Project/Scripts/Gameplay/Bootstrap");
            var files = Directory.GetFiles(dir, "DevConsole*.cs");
            Assert.IsNotEmpty(files, "the console moved — point this test at it");

            // Every method declared as returning string whose name starts with Cmd.
            var returning = new HashSet<string>();
            var decl = new Regex(@"\bstring\s+(Cmd\w+)\s*\(");
            foreach (var f in files)
                foreach (Match m in decl.Matches(File.ReadAllText(f)))
                    returning.Add(m.Groups[1].Value);
            Assert.IsNotEmpty(returning, "no string-returning command found; the regex went stale");

            // A handler that calls one of them bare, without Log around it.
            var bare = new Regex(@"Handler\s*=\s*\w+\s*=>\s*(Cmd\w+)\s*\(");
            var lost = new List<string>();
            foreach (var f in files)
                foreach (Match m in bare.Matches(File.ReadAllText(f)))
                    if (returning.Contains(m.Groups[1].Value))
                        lost.Add(Path.GetFileName(f) + ": " + m.Value);

            Assert.IsEmpty(lost,
                "These handlers drop the answer their command returns — wrap it in Log(...):\n" +
                string.Join("\n", lost));
        }
    }
}
