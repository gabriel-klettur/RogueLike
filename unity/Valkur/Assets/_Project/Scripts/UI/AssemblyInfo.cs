using System.Runtime.CompilerServices;

// Grant the EditMode test assembly access to 'internal' members
// (PlayerAbilityRowHUD.Refresh, etc.)
// One line per test assembly that can reference this one (Tests/EditMode/<Root>/). The suite
// is split by layer, so a test assembly that cannot see this assembly is not listed here.
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.UI")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Editors")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.EditorTools")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Project")]
