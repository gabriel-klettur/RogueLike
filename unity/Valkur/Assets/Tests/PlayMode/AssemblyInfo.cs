using Valkur.Tests.Support;

// Every test assembly registers the Selectable pre-grow hook for itself: an assembly-level
// NUnit action only reaches the tests of the assembly that declares it. A test assembly
// without this line is the one whose UI fixtures start failing with IndexOutOfRange in
// Selectable.OnEnable once the suite has built enough Buttons. TestLayoutConventionTests
// fails any test assembly that forgets it.
[assembly: SelectableResetTestAction]
