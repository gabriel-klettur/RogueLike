using System.Runtime.CompilerServices;

// Grant the EditMode test assembly access to 'internal' members
// (DebugSetTrack, DebugTick on MusicBeatClock, etc.)
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]
