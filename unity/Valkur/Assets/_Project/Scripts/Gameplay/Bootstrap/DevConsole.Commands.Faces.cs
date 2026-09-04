using System.Collections;
using System.Text;
using UnityEngine;
using Valkur.Data;
using Valkur.Gameplay.Chat;

namespace Valkur.Gameplay
{
    /// <summary>
    /// The <c>faces</c>, <c>face</c> and <c>faceparade</c> commands: a way to SEE every
    /// facial expression a character has, on demand, without having to talk it into one.
    ///
    /// <para>WHY THIS EXISTS. The same reason <c>SpellType.AnimationProbe</c> exists for
    /// animations. A drawing that can only be reached by saying the right thing to a
    /// language model is a drawing nobody ever checks: an author cannot confirm the art
    /// imported, cannot compare two expressions side by side, and cannot tell "this face is
    /// never chosen" from "this face is missing". Both failures are silent and both look
    /// identical from the Game view.</para>
    ///
    /// <para><c>faces</c> answers the second question outright — it reports which
    /// expressions have art of their OWN and which are resolving through
    /// <see cref="FacialExpressionFallback"/> — so an import that half worked is a line of
    /// text rather than an afternoon.</para>
    ///
    /// <para>The override is a HOLD, not a one-shot. A conversation writes the face on every
    /// reply, so an author who set one to look at it would have it taken away by the next
    /// line arriving behind them.</para>
    ///
    /// Registered from <c>DevConsole.cs::RegisterDefaults()</c>.
    /// </summary>
    public partial class DevConsole
    {
        /// <summary>Seconds each face is held by <c>faceparade</c> when no time is given.</summary>
        private const float FACE_PARADE_DEFAULT_SECONDS = 1.2f;

        /// <summary>The running parade, so a second call replaces it rather than racing it.</summary>
        private Coroutine _faceParade;

        private void RegisterFaceCommands()
        {
            RegisterCommand(new ConsoleCommand
            {
                Name     = "faces",
                Usage    = "faces",
                Help     = "list the open NPC's facial expressions and which have their own art",
                Category = "chat",
                Handler  = _ => CmdFaces()
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "face",
                Usage    = "face <expression|auto>",
                Help     = "hold one facial expression on the open chat portrait",
                Category = "chat",
                Handler  = args => CmdFace(args)
            });

            RegisterCommand(new ConsoleCommand
            {
                Name     = "faceparade",
                Usage    = "faceparade [seconds]",
                Help     = "show every facial expression in turn on the open chat portrait",
                Category = "chat",
                Handler  = args => CmdFaceParade(args)
            });
        }

        // ── faces ───────────────────────────────────────────────────────────

        private void CmdFaces()
        {
            if (!TryGetTalkingPersona(out NPCPersonaDefinition persona)) return;

            var sb = new StringBuilder();
            sb.Append(persona.displayName).Append(" — ");

            int own = 0;
            foreach (FacialExpression e in FacialExpressionFallback.All)
                if (persona.HasOwnFace(e)) own++;

            sb.Append(own).Append('/').Append(FacialExpressionFallback.All.Length)
              .Append(" expressions drawn");
            Log(sb.ToString());

            foreach (FacialExpression e in FacialExpressionFallback.All)
            {
                Sprite resolved = persona.ResolveFace(e);
                string what;

                if (persona.HasOwnFace(e))
                    what = "own art (" + resolved.name + ")";
                else if (resolved != null)
                    what = "falls back to " + resolved.name;
                else
                    what = "NOTHING — this character has no face art at all";

                Log($"  {e.ToString().ToLowerInvariant(),-9} {what}");
            }

            var chat = ChatSystem.Instance;
            Log($"  now showing: {chat.CurrentExpression.ToString().ToLowerInvariant()}" +
                (chat.ExpressionOverridden ? "  (held by 'face' — 'face auto' releases it)" : ""));
        }

        // ── face ────────────────────────────────────────────────────────────

        private void CmdFace(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Log("Usage: face <expression|auto>.  'faces' lists them.");
                return;
            }

            if (!TryGetTalkingPersona(out NPCPersonaDefinition persona)) return;

            string token = args[0];
            if (token.Equals("auto", System.StringComparison.OrdinalIgnoreCase) ||
                token.Equals("off", System.StringComparison.OrdinalIgnoreCase))
            {
                StopParade();
                ChatSystem.Instance.ReleaseExpressionOverride();
                Log("Face released — the conversation owns it again.");
                return;
            }

            if (!FacialExpressionFallback.TryParse(token, out FacialExpression expression))
            {
                Log($"'{token}' is not an expression. Try: {JoinAllExpressions()}");
                return;
            }

            StopParade();
            ChatSystem.Instance.OverrideExpression(expression);

            // Say whether the face on screen is really the one asked for. Without this the
            // command is indistinguishable from a no-op on a character whose art is missing:
            // the console reports 'angry' and the panel goes on showing neutral.
            string suffix = persona.HasOwnFace(expression)
                ? ""
                : "  (no art — showing " + DescribeResolved(persona, expression) + ")";
            Log($"Holding {expression.ToString().ToLowerInvariant()}.{suffix}");
        }

        // ── faceparade ──────────────────────────────────────────────────────

        private void CmdFaceParade(string[] args)
        {
            if (!TryGetTalkingPersona(out _)) return;

            float seconds = FACE_PARADE_DEFAULT_SECONDS;
            if (args != null && args.Length > 0 &&
                float.TryParse(args[0], System.Globalization.NumberStyles.Float,
                               System.Globalization.CultureInfo.InvariantCulture, out float parsed))
            {
                seconds = Mathf.Clamp(parsed, 0.1f, 10f);
            }

            StopParade();
            _faceParade = StartCoroutine(ParadeRoutine(seconds));
            Log($"Parading {FacialExpressionFallback.All.Length} expressions at {seconds:0.0}s each. " +
                "'face auto' stops it.");
        }

        private IEnumerator ParadeRoutine(float seconds)
        {
            foreach (FacialExpression e in FacialExpressionFallback.All)
            {
                var chat = ChatSystem.Instance;

                // The panel can be closed mid-parade — the player pressed Escape, or walked
                // away and the conversation ended. Holding a face on a character nobody is
                // talking to would leave the override latched for the NEXT conversation.
                if (chat == null || !chat.IsChatOpen) break;

                chat.OverrideExpression(e);
                Log($"  {e.ToString().ToLowerInvariant()}");
                yield return new WaitForSecondsRealtime(seconds);
            }

            _faceParade = null;
            ChatSystem.Instance?.ReleaseExpressionOverride();
        }

        private void StopParade()
        {
            if (_faceParade == null) return;
            StopCoroutine(_faceParade);
            _faceParade = null;
        }

        // ── Shared ──────────────────────────────────────────────────────────

        /// <summary>
        /// The persona of the open conversation, or false with a reason logged.
        ///
        /// Every one of these commands writes to a portrait that only exists inside the chat
        /// panel, so "no chat open" is the answer to give rather than a silent no-op.
        /// </summary>
        private bool TryGetTalkingPersona(out NPCPersonaDefinition persona)
        {
            persona = null;

            var chat = ChatSystem.Instance;
            if (chat == null || !chat.IsChatOpen)
            {
                Log("No chat is open. Walk up to an NPC and press E first.");
                return false;
            }

            persona = chat.ActivePersona;
            if (persona == null)
            {
                Log("The open chat has no persona — nothing to show a face for.");
                return false;
            }

            if (!persona.HasFaces)
            {
                Log($"{persona.displayName} has no facial art, so the panel reserves no " +
                    "portrait gutter. Run 'Valkur > Chat > Import Facial Expressions' if " +
                    "this character has a facial/ folder.");
                return false;
            }

            return true;
        }

        private static string DescribeResolved(NPCPersonaDefinition persona, FacialExpression wanted)
        {
            foreach (FacialExpression candidate in FacialExpressionFallback.Chain(wanted))
            {
                if (persona.HasOwnFace(candidate)) return candidate.ToString().ToLowerInvariant();
            }
            return "the fallback portrait";
        }

        private static string JoinAllExpressions()
        {
            var sb = new StringBuilder();
            foreach (FacialExpression e in FacialExpressionFallback.All)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(e.ToString().ToLowerInvariant());
            }
            return sb.Append(", auto").ToString();
        }
    }
}
