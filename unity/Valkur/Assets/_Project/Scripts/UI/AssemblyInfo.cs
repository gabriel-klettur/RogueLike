using System.Runtime.CompilerServices;

// Grant the EditMode test assembly access to 'internal' members
// (PlayerAbilityRowHUD.Refresh, etc.)
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]
