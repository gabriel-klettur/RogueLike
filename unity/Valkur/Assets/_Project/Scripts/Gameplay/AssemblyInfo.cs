using System.Runtime.CompilerServices;

// Grant the EditMode test assembly access to 'internal' members
// (BossBeatChoreographer.DebugForceBeat, etc.)
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]
