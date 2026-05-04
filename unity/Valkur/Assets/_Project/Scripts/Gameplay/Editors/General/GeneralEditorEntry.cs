using System;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// Logical group inside the General Editor launcher panel. Each section
    /// renders as a labelled row of buttons.
    /// </summary>
    public enum GeneralEditorSection
    {
        Editors,
        Diagnostics,
        Game,
    }

    /// <summary>
    /// One launcher button in the General Editor (ESC) overlay. Built once by
    /// <see cref="GeneralEditorRegistry"/> at activation time so the live
    /// editor singletons (TileEditorManager.Instance, …) are resolved lazily
    /// through the captured lambdas — the registry never holds direct
    /// references that could outlive a Play-Mode entry.
    /// </summary>
    public sealed class GeneralEditorEntry
    {
        public string Label { get; }
        public GeneralEditorSection Section { get; }
        public Action OnClick { get; }
        public Func<bool> IsActive { get; }
        public bool ClosesLauncher { get; }

        public GeneralEditorEntry(
            string label,
            GeneralEditorSection section,
            Action onClick,
            Func<bool> isActive = null,
            bool closesLauncher = false)
        {
            Label          = label;
            Section        = section;
            OnClick        = onClick;
            IsActive       = isActive;
            ClosesLauncher = closesLauncher;
        }
    }
}
