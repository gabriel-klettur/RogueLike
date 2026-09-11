using System.Runtime.CompilerServices;

// Grant the EditMode test assembly access to 'internal' members
// (BossBeatChoreographer.DebugForceBeat, etc.)
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]

// The player panel (Valkur.UI) draws the SAME status glyphs the bars over the head do, from
// Combat.StatusGlyphs. One table read by both, rather than a second copy that drifts.
[assembly: InternalsVisibleTo("Valkur.UI")]
