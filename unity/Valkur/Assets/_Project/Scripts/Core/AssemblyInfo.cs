using System.Runtime.CompilerServices;

// Lets the EditMode suite exercise internal-static math helpers (e.g.
// AspectRatioEnforcer.ComputeViewport) without instantiating a MonoBehaviour.
// Mirrors the same declaration in Valkur.Gameplay / Valkur.Infrastructure / Valkur.UI.
[assembly: InternalsVisibleTo("Valkur.Tests.EditMode")]
