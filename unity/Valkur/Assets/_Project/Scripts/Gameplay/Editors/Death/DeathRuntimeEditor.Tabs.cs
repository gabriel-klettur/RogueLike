using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Valkur.Core;
using Valkur.Data;
using Valkur.Gameplay.Combat.Death;
using Valkur.Gameplay.World;
using Valkur.UIKit;

namespace Valkur.Gameplay.Editors.Death
{
    public partial class DeathRuntimeEditor
    {
        /// <summary>
        /// Rebuild the right-hand panel for the active tab.
        ///
        /// <para>Destroying with the Play/Edit branch is mandatory: <c>Object.Destroy</c> is an
        /// outright ERROR in Edit Mode — not a warning — and seven Controls-editor tests went red
        /// on that log line alone with every assertion passing.</para>
        /// </summary>
        private void RebuildBody()
        {
            if (_bodyScrollContent == null) return;

            _fieldResync.Clear();
            for (int i = _bodyScrollContent.childCount - 1; i >= 0; i--)
            {
                var child = _bodyScrollContent.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }

            var body = _bodyScrollContent;
            switch (_tab)
            {
                case Tab.Flow:   BuildFlowTab(body);   break;
                case Tab.Spirit: BuildSpiritTab(body); break;
                case Tab.Altars: BuildAltarsTab(body); break;
                case Tab.Path:   BuildPathTab(body);   break;
                case Tab.Cost:   BuildCostTab(body);   break;
                case Tab.Audio:  BuildAudioTab(body);  break;
            }
        }

        // ── Flow ────────────────────────────────────────────────────────────

        private void BuildFlowTab(Transform body)
        {
            var t = _tuning;
            EditorUIHelpers.BuildSectionHeader(body, "Ritmo");

            AddFloatField(body, "Pausa al morir (s)",
                "Entre morir y poder mover el espiritu. Por debajo de 0.3 s el jugador no llega a " +
                "leer que ha muerto; por encima de 1.2 s se lee como que el juego se ha colgado.",
                () => t.dyingFlashDuration, v => t.dyingFlashDuration = v, 0f, 3f);

            AddFloatField(body, "Fundido a gris (s)", "Cuanto tarda el mundo en drenarse de color.",
                () => t.grayscaleFadeIn, v => t.grayscaleFadeIn = v, 0f, 5f);

            AddFloatField(body, "Fundido de vuelta (s)",
                "Tambien es la espera del revivir: subirlo alarga el tiempo sin control despues " +
                "de tocar el altar.",
                () => t.grayscaleFadeOut, v => t.grayscaleFadeOut = v, 0f, 5f);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Probar de verdad");

            AddHintLine(body,
                "Estos botones ejecutan el flujo REAL: sueltan el inventario, gastan el coste y " +
                "avisan a todos los sistemas. Un camino de prueba que se salta la mitad del flujo " +
                "prueba la mitad del flujo.");

            var row = MakeRow(body, "FlowTestRow", 30f);
            EditorUIHelpers.MakeButton(row.transform, "MATAR", TestKill, 30f, 11f);
            EditorUIHelpers.MakeButton(row.transform, "REVIVIR", TestRevive, 30f, 11f);
            EditorUIHelpers.MakeButton(row.transform, "RESCATAR", TestRescue, 30f, 11f);

            var row2 = MakeRow(body, "FlowTestRow2", 30f);
            EditorUIHelpers.MakeButton(row2.transform, "IR AL ALTAR MAS CERCANO", TeleportToNearestAltar, 30f, 11f);
            EditorUIHelpers.MakeButton(row2.transform, "IR AL CADAVER", TeleportToCorpse, 30f, 11f);

            EditorUIHelpers.BuildSeparator(body);
            var resetRow = MakeRow(body, "ResetRow", 30f);
            EditorUIHelpers.MakeDangerButton(resetRow.transform, "VALORES POR DEFECTO", ResetToDefaults, 30f);
        }

        private void TestKill()
        {
            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null) { SetStatus("No hay DeathSequenceController en la escena."); return; }

            if (controller.KillPlayerForTesting(out string refusal)) SetStatus("Jugador muerto. Mira el estado en vivo.");
            else SetStatus(refusal);
        }

        private void TestRevive()
        {
            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null) { SetStatus("No hay DeathSequenceController en la escena."); return; }
            if (controller.CurrentPhase == DeathSequenceController.Phase.Alive)
            { SetStatus("El jugador ya esta vivo."); return; }

            controller.ForceRevive();
            SetStatus("Revivido (camino de autor: instantaneo y, por defecto, sin coste).");
        }

        private void TestRescue()
        {
            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null) { SetStatus("No hay DeathSequenceController en la escena."); return; }

            if (controller.Rescue("probado desde el editor de Muerte"))
                SetStatus($"Rescate lanzado: modo {_tuning.rescueMode}, " +
                          $"{Mathf.RoundToInt(_tuning.rescueHpFraction * 100f)}% de vida.");
            else
                SetStatus($"El rescate solo actua en forma espiritu (fase: {controller.CurrentPhase}).");
        }

        // ── Spirit ──────────────────────────────────────────────────────────

        private void BuildSpiritTab(Transform body)
        {
            var t = _tuning;
            EditorUIHelpers.BuildSectionHeader(body, "Como se mueve");

            AddFloatField(body, "Velocidad (x)",
                "Multiplica la velocidad compuesta del personaje, no la sustituye: un personaje " +
                "rapido y uno lento seguirian siendo distintos al morir.",
                () => t.spiritSpeedMultiplier, v => t.spiritSpeedMultiplier = v, 0.25f, 3f);

            AddBoolField(body, "Muros",
                "Tiene que ir de la mano con el modo de camino. Una linea recta que cruza paredes " +
                "por las que el espiritu NO pasa es una ruta que promete algo que no se puede andar.",
                () => t.spiritPassesThroughWalls, v => t.spiritPassesThroughWalls = v,
                "LOS ATRAVIESA", "ES SOLIDO");

            AddBoolField(body, "Ante los NPC",
                "Encendido, la vuelta al altar es una huida. Apagado (lo normal) los monstruos " +
                "ignoran al espiritu por completo.",
                () => t.spiritIsTargetable, v => t.spiritIsTargetable = v,
                "ATACABLE", "INTANGIBLE");

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Limite");

            AddFloatField(body, "Tiempo maximo (s)",
                "0 = sin limite. Con altar alcanzable es un colchon; sin el, es la unica cosa que " +
                "impide que una muerte no tenga salida.",
                () => t.spiritTimeLimitSeconds, v => t.spiritTimeLimitSeconds = v, 0f, 600f);

            AddHintLine(body,
                "Cambiar 'muros' con un espiritu ya en pantalla se aplica en la siguiente muerte: " +
                "la mascara del collider se toma al entrar en forma espiritu.");
        }

        // ── Altars ──────────────────────────────────────────────────────────

        /// <summary>
        /// The tab that fixes the shipped bug.
        ///
        /// <para>It is built around one action — "usa la plantilla del edificio que tengo delante"
        /// — because the failure was never that the author chose the wrong template id, it was that
        /// choosing one at all required knowing that a number in a C# file existed and what
        /// building it referred to. Standing next to the arch and clicking once is the whole
        /// interaction.</para>
        /// </summary>
        private void BuildAltarsTab(Transform body)
        {
            var t = _tuning;

            EditorUIHelpers.BuildSectionHeader(body, "Estado");

            int altars = ResurrectionAltarRegistry.Count;
            var binder = FindObjectOfType<ResurrectionZoneAutoBinder>();

            var stateLabel = EditorUIHelpers.AddLabel(body,
                altars > 0
                    ? $"<color=#6fbf73>{altars} altar(es) en el mundo cargado.</color>"
                    : "<color=#d05a5a>NINGUN altar en el mundo cargado. El jugador que muera solo " +
                      "puede volver por el rescate.</color>",
                11f);
            stateLabel.enableWordWrapping = true;
            var stateLayout = stateLabel.gameObject.AddComponent<LayoutElement>();
            stateLayout.preferredHeight = 34f;
            stateLayout.flexibleHeight = 0f;

            if (binder != null)
                AddHintLine(body, binder.IsSearching
                    ? "El vinculador sigue buscando edificios (el mundo se carga por partes)."
                    : $"Vinculador terminado: {binder.BoundCount} vinculado(s).");

            EditorUIHelpers.BuildSectionHeader(body, "Edificios que revi​ven");

            AddHintLine(body,
                "Ser altar es una propiedad del EDIFICIO, como 'tiene puerta': vive en la plantilla " +
                "y por tanto afecta a todas sus colocaciones. Aqui se listan las plantillas que hoy " +
                "lo son, y donde tiene que ponerse el espiritu en cada una.");

            foreach (var tpl in AltarTemplatesInWorld())
            {
                var row = MakeRow(body, $"Tpl{tpl.templateId}Row", ROW_H + 4f);
                EditorUIKit.AddCaption(row.transform,
                    $"{tpl.templateId} · {ShortAssetName(tpl.assetPath)} — {DescribeTemplate(tpl.templateId)}", 330f);
                EditorUIHelpers.MakeDangerButton(row.transform, "QUITAR",
                    () => SetTemplateIsAltar(tpl, false), ROW_H + 4f);

                AddAnchorRow(body, tpl);
            }

            if (!AnyAltarTemplateInWorld())
                AddHintLine(body, "Ninguna. Sin al menos una, nada del mundo puede ser un altar.");

            EditorUIHelpers.BuildSeparator(body);

            var addRow = MakeRow(body, "AddTplRow", 30f);
            EditorUIHelpers.MakeButton(addRow.transform, "USAR EL EDIFICIO MAS CERCANO",
                AddNearestBuildingAsAltar, 30f, 11f);

            AddHintLine(body,
                "Ponte al lado del edificio que quieras usar como altar y pulsa el boton: lee su " +
                "plantilla, la anade a la lista y vincula todos los edificios iguales del mundo, " +
                "sin tener que saber ningun numero.");

            AddIntFieldWithButton(body, "Anadir por id (legacy)", AddTemplateById);
            AddHintLine(body,
                "Escribe la propiedad en la plantilla igual que el boton de arriba. El id sigue " +
                "aqui porque a veces se tiene el numero y no el edificio delante — pero mirar un " +
                "numero contra un edificio que no ves es exactamente como se envio el fallo que " +
                "reconstruyo este sistema.");

            EditorUIHelpers.BuildSeparator(body);

            var rebindRow = MakeRow(body, "RebindRow", 30f);
            EditorUIHelpers.MakeButton(rebindRow.transform, "REVINCULAR AHORA", RebindAltars, 30f, 11f);
            EditorUIHelpers.MakeButton(rebindRow.transform, "IR AL MAS CERCANO", TeleportToNearestAltar, 30f, 11f);

            EditorUIHelpers.BuildSectionHeader(body, "Ajustes del altar");

            AddFloatField(body, "Margen de activacion (u)",
                "Radio extra alrededor de la huella. A 0 hay que pisar el rectangulo exacto del " +
                "edificio, que en un arco estrecho es aproximadamente una baldosa.",
                () => t.altarActivationPadding, v => t.altarActivationPadding = v, 0f, 4f);

            AddFloatField(body, "Busqueda del vinculador (s)",
                "Cuanto busca antes de rendirse. El mundo se carga por partes, asi que un solo " +
                "barrido al arrancar encuentra un mundo a medias.",
                () => t.altarSearchTimeout, v => t.altarSearchTimeout = v, 5f, 300f);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Rescate: la garantia de que hay salida");

            AddEnumField(body, "Donde revive",
                "El unico ajuste del que depende que una muerte no pueda dejar la partida sin " +
                "salida. 'Ninguno' solo es legitimo con un altar garantizado alcanzable.",
                () => t.rescueMode, v => t.rescueMode = v,
                (DeathRescueMode.None, "NUNCA"),
                (DeathRescueMode.LastCheckpoint, "CHECKPOINT"),
                (DeathRescueMode.DeathPosition, "AL MORIR"),
                (DeathRescueMode.ZoneSpawn, "ZONA"));

            AddFloatField(body, "Espera sin altar (s)",
                "Cuando NO existe ningun altar cargado. Corto a proposito: no hay nada que esperar.",
                () => t.rescueDelayWithoutAltar, v => t.rescueDelayWithoutAltar = v, 1f, 120f);

            AddFloatField(body, "Vida al rescatar (0-1)",
                "Menor que 1 para que el rescate siga siendo peor que llegar al altar, que revive " +
                "al maximo. A 1 el altar deja de merecer el paseo.",
                () => t.rescueHpFraction, v => t.rescueHpFraction = v, 0.05f, 1f);
        }

        private string DescribeTemplate(int id)
        {
            var loader = FindObjectOfType<BuildingLoader>();
            if (loader == null) return "(sin BuildingLoader)";

            int placed = 0;
            string assetPath = null;
            var spawned = loader.SpawnedBuildings;
            for (int i = 0; i < spawned.Count; i++)
            {
                var b = spawned[i];
                if (b == null || b.Template == null || b.Template.templateId != id) continue;
                placed++;
                if (assetPath == null) assetPath = b.Template.assetPath;
            }

            if (placed == 0) return "<color=#d05a5a>0 colocados en este mundo</color>";
            return $"{placed} colocado(s) · {assetPath}";
        }

        /// <summary>
        /// Every template in the loaded world that is an altar, ordered so the list does not
        /// reshuffle under the author between refreshes.
        /// </summary>
        private List<BuildingTemplateData> AltarTemplatesInWorld()
        {
            var found = new List<BuildingTemplateData>();
            var loader = FindObjectOfType<BuildingLoader>();
            if (loader == null) return found;

            var spawned = loader.SpawnedBuildings;
            for (int i = 0; i < spawned.Count; i++)
            {
                var b = spawned[i];
                if (b == null || b.Template == null) continue;
                if (!ResurrectionAltarRegistry.IsAltar(b.Template)) continue;
                if (!found.Contains(b.Template)) found.Add(b.Template);
            }
            found.Sort((a, c) => a.templateId.CompareTo(c.templateId));
            return found;
        }

        private bool AnyAltarTemplateInWorld() => AltarTemplatesInWorld().Count > 0;

        private static string ShortAssetName(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return "(sin arte)";
            int slash = assetPath.LastIndexOf('/');
            return slash >= 0 && slash < assetPath.Length - 1 ? assetPath.Substring(slash + 1) : assetPath;
        }

        /// <summary>
        /// The four anchors as four buttons, per template.
        ///
        /// <para>All four visible rather than behind a dropdown: the choice is spatial and the
        /// author is deciding between four PLACES, so hiding three of them behind a click makes
        /// them a thing to remember instead of a thing to compare. Same reasoning as the rescue
        /// mode row.</para>
        /// </summary>
        private void AddAnchorRow(Transform body, BuildingTemplateData tpl)
        {
            var row = MakeRow(body, $"Tpl{tpl.templateId}Anchor", ROW_H + 4f);
            EditorUIKit.AddCaption(row.transform, "    donde", 110f);

            AddAnchorButton(row.transform, tpl, ResurrectionAnchor.Proximity, "CERCA");
            AddAnchorButton(row.transform, tpl, ResurrectionAnchor.Base,      "BASE");
            AddAnchorButton(row.transform, tpl, ResurrectionAnchor.Center,    "CENTRO");
            AddAnchorButton(row.transform, tpl, ResurrectionAnchor.Top,       "ARRIBA");

            if (tpl.resurrectionAnchor == ResurrectionAnchor.Proximity)
                AddFloatField(body, "    radio (u)",
                    "Cuanto se aleja la zona del edificio, por cualquier lado. Solo lo usa CERCA.",
                    () => tpl.resurrectionRadius,
                    v => { tpl.resurrectionRadius = v; MarkTemplateDirty(tpl); }, 0.25f, 8f);
            else if (tpl.solid)
                AddHintLine(body,
                    "    Este edificio es SOLIDO: la rejilla de colision decide donde se puede " +
                    "estar, y base/centro/arriba caen dentro de el. Funciona si su arte tiene un " +
                    "hueco pintado como transitable — un arco lo tiene, una casa no. CERCA es la " +
                    "unica que siempre se alcanza.");
        }

        private void AddAnchorButton(Transform parent, BuildingTemplateData tpl,
                                     ResurrectionAnchor anchor, string caption)
        {
            Button button = null;
            button = NoWrap(EditorUIHelpers.MakeButton(parent, caption, () =>
            {
                if (tpl.resurrectionAnchor == anchor) return;
                var before = tpl.resurrectionAnchor;
                Commit($"Altar {tpl.templateId}: {ResurrectionZoneGeometry.Describe(anchor)}",
                    () => { tpl.resurrectionAnchor = before; MarkTemplateDirty(tpl); },
                    () => { tpl.resurrectionAnchor = anchor; MarkTemplateDirty(tpl); });
                RefreshAll();
            }, ROW_H + 4f, 10f));

            UIButton.SetTint(button,
                tpl.resurrectionAnchor == anchor ? UITheme.BTN_ACTIVE : UITheme.BTN_NORMAL);
        }

        /// <summary>
        /// Turn a template's altar flag on or off, undoably.
        ///
        /// <para>It also CLEARS the legacy id, because leaving the two disagreeing is the whole
        /// defect this replaced: switching the flag off while the tuning list still names the id
        /// would report "no longer an altar" and go on reviving people. The predicate ORs them, so
        /// the only honest way to say no is to say it in both places.</para>
        /// </summary>
        private void SetTemplateIsAltar(BuildingTemplateData tpl, bool value)
        {
            if (tpl == null) return;

            bool beforeFlag = tpl.isResurrectionAltar;
            bool beforeLegacy = _tuning.IsAltarTemplate(tpl.templateId);
            if (beforeFlag == value && beforeLegacy == value) return;

            Commit($"Altar {tpl.templateId}: {(value ? "encendido" : "apagado")}",
                () =>
                {
                    tpl.isResurrectionAltar = beforeFlag;
                    if (beforeLegacy) _tuning.AddAltarTemplate(tpl.templateId);
                    else _tuning.RemoveAltarTemplate(tpl.templateId);
                    MarkTemplateDirty(tpl);
                    RebindAltarsQuiet();
                },
                () =>
                {
                    tpl.isResurrectionAltar = value;
                    if (!value) _tuning.RemoveAltarTemplate(tpl.templateId);
                    MarkTemplateDirty(tpl);
                    RebindAltarsQuiet();
                });

            RefreshAll();
            SetStatus(value
                ? $"Plantilla {tpl.templateId} es altar, {ResurrectionZoneGeometry.Describe(tpl.resurrectionAnchor)}."
                : $"Plantilla {tpl.templateId} ya no es altar. Altares en el mundo: {ResurrectionAltarRegistry.Count}.");
        }

        /// <summary>
        /// A template edited from here is a shipped ASSET, so it has to be marked dirty or the
        /// change dies with Play Mode — the same contract the Buildings editor's door panel keeps.
        /// </summary>
        private static void MarkTemplateDirty(BuildingTemplateData tpl)
        {
#if UNITY_EDITOR
            if (tpl != null) UnityEditor.EditorUtility.SetDirty(tpl);
#endif
        }

        /// <summary>
        /// The legacy door in: an id, with no building in front of you.
        ///
        /// <para>It writes the FLAG when the template can be found in the loaded world, and falls
        /// back to the tuning list only when it cannot — a template that is not spawned here has no
        /// asset this editor can reach.</para>
        /// </summary>
        private void AddTemplateById(int id)
        {
            if (id <= 0) { SetStatus("Un id de plantilla es positivo."); return; }

            var loader = FindObjectOfType<BuildingLoader>();
            BuildingTemplateData tpl = null;
            if (loader != null)
            {
                var spawned = loader.SpawnedBuildings;
                for (int i = 0; i < spawned.Count && tpl == null; i++)
                    if (spawned[i] != null && spawned[i].Template != null &&
                        spawned[i].Template.templateId == id) tpl = spawned[i].Template;
            }

            if (tpl != null)
            {
                if (ResurrectionAltarRegistry.IsAltar(tpl))
                { SetStatus($"La plantilla {id} ya era altar."); return; }
                SetTemplateIsAltar(tpl, true);
                return;
            }

            if (_tuning.IsAltarTemplate(id)) { SetStatus($"La plantilla {id} ya estaba en la lista."); return; }

            Commit($"Altar: anadida la plantilla {id} (lista legacy)",
                () => { _tuning.RemoveAltarTemplate(id); RebindAltarsQuiet(); },
                () => { _tuning.AddAltarTemplate(id); RebindAltarsQuiet(); });
            RefreshAll();
            SetStatus($"Plantilla {id} anadida a la lista legacy: no hay ninguna colocada en este " +
                      "mundo, asi que no hay asset que marcar.");
        }

        /// <summary>
        /// Read the template of the building nearest the player and make it an altar template.
        ///
        /// <para>Nearest by the building's own RECT rather than its transform: a pivot sits at a
        /// corner of a footprint several tiles across, so on the transform alone the author
        /// standing in a doorway can be told they are next to the house behind them.</para>
        /// </summary>
        private void AddNearestBuildingAsAltar()
        {
            var loader = FindObjectOfType<BuildingLoader>();
            if (loader == null) { SetStatus("No hay BuildingLoader en la escena."); return; }

            var player = EntityRegistry.PlayerTransform;
            if (player == null) { SetStatus("No hay jugador: no se desde donde medir."); return; }

            BuildingObject nearest = null;
            float best = float.PositiveInfinity;
            var spawned = loader.SpawnedBuildings;
            for (int i = 0; i < spawned.Count; i++)
            {
                var b = spawned[i];
                if (b == null || b.Template == null) continue;

                Vector2 anchor = b.TryGetWorldRect(out Rect rect) ? rect.center : (Vector2)b.transform.position;
                float d = Vector2.Distance(player.position, anchor);
                if (d < best) { best = d; nearest = b; }
            }

            if (nearest == null) { SetStatus("No hay ningun edificio colocado en este mundo."); return; }

            int id = nearest.Template.templateId;
            if (_tuning.IsAltarTemplate(id))
            {
                SetStatus($"El edificio mas cercano ({nearest.Template.assetPath}, plantilla {id}) " +
                          "ya era un altar.");
                return;
            }

            Commit($"Altar: la plantilla {id} ({nearest.Template.assetPath}) pasa a ser altar",
                () => { _tuning.RemoveAltarTemplate(id); RebindAltarsQuiet(); },
                () => { _tuning.AddAltarTemplate(id); RebindAltarsQuiet(); });

            RefreshAll();
            SetStatus($"Plantilla {id} ({nearest.Template.assetPath}) a {best:0.0} u es ahora altar. " +
                      $"{ResurrectionAltarRegistry.Count} altar(es) en el mundo.");
        }

        private void RebindAltars()
        {
            RebindAltarsQuiet();
            SetStatus($"Revinculado: {ResurrectionAltarRegistry.Count} altar(es) en el mundo cargado.");
            RefreshAll();
        }

        /// <summary>
        /// Re-attach and detach <c>ResurrectionZone</c>s to match the template list, and re-arm the
        /// binder.
        ///
        /// <para>Both halves are needed. The rebind reaches the buildings that are ALREADY spawned,
        /// so the change is visible in the same frame instead of on the next world load; the re-arm
        /// reaches the ones the loader has not streamed in yet, and clears the "I gave up" flag that
        /// would otherwise keep the banner saying there is no altar.</para>
        /// </summary>
        private void RebindAltarsQuiet()
        {
            ResurrectionAltarRegistry.Rebind(FindObjectOfType<BuildingLoader>());
            FindObjectOfType<ResurrectionZoneAutoBinder>()?.Rearm();
        }

        private void TeleportToNearestAltar()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { SetStatus("No hay jugador."); return; }

            if (!ResurrectionAltarRegistry.TryGetNearest(player.position, out var altar, out float d))
            { SetStatus("No hay ningun altar al que ir."); return; }

            MovePlayer(player, altar.AnchorPoint);
            SetStatus($"Llevado al altar a {d:0.0} u.");
        }

        private void TeleportToCorpse()
        {
            var player = EntityRegistry.PlayerTransform;
            if (player == null) { SetStatus("No hay jugador."); return; }

            var controller = ServiceLocator.Get<DeathSequenceController>();
            if (controller == null || controller.ActiveCorpse == null)
            { SetStatus("No hay ningun cadaver ahora mismo."); return; }

            MovePlayer(player, controller.ActiveCorpse.transform.position);
            SetStatus("Llevado al cadaver.");
        }

        /// <summary>
        /// Move the player, zeroing the velocity and WAKING the body — a Dynamic body that has come
        /// to rest sleeps after half a second, and a sleeping body starts no new contacts, so a
        /// player dropped onto an altar footprint would never trigger it.
        /// </summary>
        private static void MovePlayer(Transform player, Vector2 destination)
        {
            player.position = new Vector3(destination.x, destination.y, player.position.z);
            var rb = player.GetComponent<Rigidbody2D>();
            if (rb != null) { rb.velocity = Vector2.zero; rb.WakeUp(); }
        }

        // ── Path ────────────────────────────────────────────────────────────

        private void BuildPathTab(Transform body)
        {
            var t = _tuning;
            EditorUIHelpers.BuildSectionHeader(body, "Camino al altar");

            AddEnumField(body, "Forma",
                "RECTA es una brujula magica que cruza muros: correcta exactamente cuando el " +
                "espiritu tambien puede. RUTA pasa por el PathFinder y es la honesta con un " +
                "espiritu solido.",
                () => t.pathMode, v => t.pathMode = v,
                (SpiritPathMode.None, "NINGUNO"),
                (SpiritPathMode.StraightLine, "RECTA"),
                (SpiritPathMode.Pathfound, "RUTA"));

            AddFloatField(body, "Recalculo (s)", "Cada cuanto se rehace el rastro.",
                () => t.pathUpdateInterval, v => t.pathUpdateInterval = v, 0.05f, 2f);

            AddIntField(body, "Tope de baldosas",
                "Recorta por el extremo LEJANO, asi que lo que se ve siempre es el tramo pegado al " +
                "jugador. Recortar por el cercano dejaria una cinta flotando en la niebla.",
                () => t.pathMaxMarkers, v => t.pathMaxMarkers = v, 8, 600);

            AddColorRow(body, "Color del camino", () => t.pathTint, v => t.pathTint = v);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Brujula al cadaver");

            AddBoolField(body, "Rastro al cuerpo",
                "Sin el, el jugador revive y no tiene forma de saber donde dejo su inventario y sus " +
                "monedas: se midieron 64 unidades entre el espiritu y su propio cadaver.",
                () => t.showCorpseCompass, v => t.showCorpseCompass = v, "SI", "NO");

            AddColorRow(body, "Color del cadaver", () => t.corpseTint, v => t.corpseTint = v);

            AddHintLine(body,
                "Los dos rastros son de colores distintos a proposito: son dos destinos, y un solo " +
                "color los convierte en uno.");
        }

        // ── Cost ────────────────────────────────────────────────────────────

        private void BuildCostTab(Transform body)
        {
            var t = _tuning;
            EditorUIHelpers.BuildSectionHeader(body, "Que se cae al morir");

            AddBoolField(body, "Inventario", "Todo el inventario se cae en el punto de la muerte.",
                () => t.dropInventory, v => t.dropInventory = v, "SE CAE", "SE CONSERVA");

            AddBoolField(body, "Monedas", null,
                () => t.dropCoins, v => t.dropCoins = v, "SE CAEN", "SE CONSERVAN");

            AddFloatField(body, "Fraccion de la bolsa (0-1)",
                "1 = toda. Por debajo, el jugador conserva un colchon; se redondea hacia abajo, " +
                "para que el colchon exista tambien con bolsas pequenas.",
                () => t.coinLossFraction, v => t.coinLossFraction = v, 0f, 1f);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Experiencia");

            AddFloatField(body, "XP perdida (0-1)",
                "Fraccion de la XP del NIVEL ACTUAL, no de la total: la perdida escala con el " +
                "progreso y no con la vida entera del personaje.",
                () => t.xpLossFraction, v => t.xpLossFraction = v, 0f, 1f);

            AddBoolField(body, "Puede bajar de nivel", null,
                () => t.xpLossCanDelevel, v => t.xpLossCanDelevel = v, "SI", "NO");

            AddBoolField(body, "El cheat cobra",
                "Revivir por consola o desde este editor es un camino de autor. Cobrarlo era un " +
                "defecto real: se cobraba el 10% de la XP por usarlo.",
                () => t.cheatRevivePaysCost, v => t.cheatRevivePaysCost = v, "SI COBRA", "ES GRATIS");

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Cadaver y botin en el suelo");

            AddFloatField(body, "El cadaver aguanta (s)",
                "Segundos que el cuerpo sobrevive tras revivir. 0 = desaparece al revivir, y con " +
                "el desaparece la unica marca sobre el monton de objetos.",
                () => t.corpseLingerSeconds, v => t.corpseLingerSeconds = v, 0f, 600f);

            AddBoolField(body, "Barrer al morir otra vez",
                "Se barre en la muerte SIGUIENTE, nunca al revivir: barrer al revivir borraria el " +
                "monton al que el jugador acaba de volver andando.",
                () => t.cleanupPreviousDrops, v => t.cleanupPreviousDrops = v, "SI", "NO");

            var litterRow = MakeRow(body, "LitterRow", 30f);
            EditorUIKit.AddCaption(litterRow.transform, $"{DeathLitter.Count} objeto(s) en el suelo", 260f);
            EditorUIHelpers.MakeDangerButton(litterRow.transform, "BARRER AHORA", () =>
            {
                int removed = DeathLitter.ClearAll();
                SetStatus($"Retirados {removed} objeto(s).");
                RefreshAll();
            }, 30f);

            EditorUIHelpers.BuildSeparator(body);
            EditorUIHelpers.BuildSectionHeader(body, "Persistencia");

            AddBoolField(body, "Guardar la muerte",
                "Apagado, morir + salir + recargar DESHACE la muerte entera y devuelve el " +
                "inventario: todo lo de arriba pasa a ser opcional para el jugador.",
                () => t.persistDeathState, v => t.persistDeathState = v, "SI", "NO");
        }

        // ── Audio ───────────────────────────────────────────────────────────

        private void BuildAudioTab(Transform body)
        {
            var t = _tuning;
            EditorUIHelpers.BuildSectionHeader(body, "Sonidos del flujo");

            AddHintLine(body,
                "Un id vacio, o que el catalogo no tenga, es silencio y NO produce warning: se " +
                "consulta con HasSfx antes de pedirlo. El catalogo enviado no trae ninguno de " +
                "estos, asi que hoy los tres estan en silencio a proposito.");

            AddSfxField(body, "Al morir", () => t.sfxDeath, v => t.sfxDeath = v);
            AddSfxField(body, "Al entrar en espiritu", () => t.sfxSpiritEnter, v => t.sfxSpiritEnter = v);
            AddSfxField(body, "Al revivir", () => t.sfxRevive, v => t.sfxRevive = v);
        }

        /// <summary>
        /// An SFX id plus a live verdict on whether the catalogue actually has it.
        ///
        /// <para>The verdict is the whole point: an id that resolves to nothing behaves exactly
        /// like an id that resolves to silence, and without something on screen saying which, an
        /// author tunes a sound that was never going to play.</para>
        /// </summary>
        private void AddSfxField(Transform body, string label, System.Func<string> get, System.Action<string> set)
        {
            AddTextField(body, label, null, get, set);

            var audio = ServiceLocator.Get<IAudioService>();
            string id = get();
            string verdict;
            if (string.IsNullOrWhiteSpace(id)) verdict = "vacio: silencio";
            else if (audio == null) verdict = "no hay servicio de audio en esta escena";
            else verdict = audio.HasSfx(id)
                ? "<color=#6fbf73>esta en el catalogo</color>"
                : "<color=#d08a3a>no esta en el catalogo: sonara silencio</color>";

            AddHintLine(body, verdict);
        }

        // ── Small shared widgets ────────────────────────────────────────────

        private static void AddHintLine(Transform parent, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var hint = EditorUIHelpers.AddLabel(parent, text, 9.5f);
            hint.color = UITheme.TEXT_MUTED;
            hint.enableWordWrapping = true;
            var element = hint.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = 34f;
            element.minHeight = 14f;
            element.flexibleHeight = 0f;
        }

        /// <summary>A numeric box whose value is only consumed when its button is pressed.</summary>
        private void AddIntFieldWithButton(Transform parent, string label, System.Action<int> onSubmit)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 6f);
            EditorUIKit.AddCaption(row.transform, label, 200f);

            string pending = string.Empty;
            var field = EditorUIHelpers.AddInputField(row.transform, "", text => pending = text);
            EditorUIHelpers.MakeButton(row.transform, "ANADIR", () =>
            {
                string raw = string.IsNullOrEmpty(pending) ? field.text : pending;
                if (int.TryParse(raw, out int id)) onSubmit(id);
                else SetStatus("Escribe un id de plantilla numerico.");
                field.SetTextWithoutNotify("");
                pending = string.Empty;
            }, ROW_H + 6f, 10f);
        }

        /// <summary>
        /// A colour as three 0-255 boxes.
        ///
        /// <para>No colour picker in the kit, and three boxes is honest rather than a placeholder:
        /// both of these colours are read at a glance on a desaturated world, so what an author
        /// needs is to nudge one channel and look, not to browse a wheel. Alpha is deliberately not
        /// exposed — the marker sprite carries its own fill alpha and a second one multiplied on top
        /// is how a trail becomes invisible with nothing saying why.</para>
        /// </summary>
        private void AddColorRow(Transform parent, string label, System.Func<Color> get, System.Action<Color> set)
        {
            var row = MakeRow(parent, label + "Row", ROW_H + 4f);
            EditorUIKit.AddCaption(row.transform, label, 150f);

            AddChannel(row.transform, "R", () => get().r, v => { var c = get(); c.r = v; set(c); });
            AddChannel(row.transform, "G", () => get().g, v => { var c = get(); c.g = v; set(c); });
            AddChannel(row.transform, "B", () => get().b, v => { var c = get(); c.b = v; set(c); });

            var swatchGo = EditorUIHelpers.CreateUI(label + "Swatch", row.transform);
            var swatch = swatchGo.AddComponent<Image>();
            swatch.color = get();
            var swatchLayout = swatchGo.AddComponent<LayoutElement>();
            swatchLayout.preferredWidth = 36f;
            swatchLayout.flexibleWidth = 0f;
            _fieldResync.Add(() => { if (swatch != null) swatch.color = get(); });
        }

        private void AddChannel(Transform parent, string name, System.Func<float> get, System.Action<float> set)
        {
            var field = EditorUIHelpers.AddInputField(parent,
                Mathf.RoundToInt(get() * 255f).ToString(), text =>
            {
                if (!int.TryParse(text, out int v)) { RefreshAfterEdit(); return; }
                float next = Mathf.Clamp01(v / 255f);
                float before = get();
                if (Mathf.Approximately(before, next)) { RefreshAfterEdit(); return; }

                Commit($"Color {name}: {Mathf.RoundToInt(before * 255f)} -> {v}",
                    () => set(before), () => set(next));
            });

            var layout = field.gameObject.AddComponent<LayoutElement>();
            layout.preferredWidth = 46f;
            layout.flexibleWidth = 0f;
            _fieldResync.Add(() => field.SetTextWithoutNotify(Mathf.RoundToInt(get() * 255f).ToString()));
        }
    }
}
