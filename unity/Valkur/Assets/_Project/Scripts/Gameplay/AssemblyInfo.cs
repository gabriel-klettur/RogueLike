using System.Runtime.CompilerServices;

// Grant the EditMode test assembly access to 'internal' members
// (BossBeatChoreographer.DebugForceBeat, etc.)
// One line per test assembly that can reference this one (Tests/EditMode/<Root>/). The suite
// is split by layer, so a test assembly that cannot see this assembly is not listed here.
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Gameplay")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.UI")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Editors")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.EditorTools")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Project")]

// The player panel (Valkur.UI) draws the SAME status glyphs the bars over the head do, from
// Combat.StatusGlyphs. One table read by both, rather than a second copy that drifts.
[assembly: InternalsVisibleTo("Valkur.UI")]
