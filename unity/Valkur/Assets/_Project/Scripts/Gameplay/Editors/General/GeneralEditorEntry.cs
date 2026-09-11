using System;

namespace Valkur.Gameplay.Editors.General
{
    /// <summary>
    /// Logical group inside the General Editor launcher panel. Each section is one TAB, and
    /// the enum is the tab order.
    ///
    /// <para><see cref="Tools"/> was called <c>Diagnostics</c> and held three overlay toggles.
    /// The rename is what it always was: none of the three is an editor, and "Map Backups"
    /// had been filed under <see cref="Game"/> — beside Save, Load and Quit — while being a
    /// browser rather than a session action. A tool is a thing that acts ON the world without
    /// being a modal authoring surface for one kind of content; that is the line the three
    /// sections are drawn on now.</para>
    /// </summary>
    public enum GeneralEditorSection
    {
        Editors,
        Tools,
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
