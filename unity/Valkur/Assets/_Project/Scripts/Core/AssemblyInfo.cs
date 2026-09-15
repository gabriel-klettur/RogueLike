using System.Runtime.CompilerServices;

// Lets the EditMode suite exercise internal-static math helpers (e.g.
// AspectRatioEnforcer.ComputeViewport) without instantiating a MonoBehaviour.
// Mirrors the same declaration in Valkur.Gameplay / Valkur.Infrastructure / Valkur.UI.
// One line per test assembly that can reference this one (Tests/EditMode/<Root>/). The suite
// is split by layer, so a test assembly that cannot see this assembly is not listed here.
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Core")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Data")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Infrastructure")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.UIKit")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Gameplay")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.UI")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Editors")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.EditorTools")]
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode.Project")]
