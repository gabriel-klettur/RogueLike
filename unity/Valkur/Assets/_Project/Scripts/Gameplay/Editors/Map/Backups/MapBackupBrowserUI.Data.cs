using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Valkur.Gameplay.MapEditor.Backups
{
    public partial class MapBackupBrowserUI
    {
        // ── List rendering ───────────────────────────────────────────────────────

        private void RefreshList()
        {
            _backups = _store.ListBackups();
            for (int i = _listContent.childCount - 1; i >= 0; i--)
                Destroy(_listContent.GetChild(i).gameObject);

            if (_backups.Count == 0)
            {
                var empty = AddText(_listContent, "(no backups yet — click Create above)",
                                    12f, TextDim, TextAlignmentOptions.Center, FontStyles.Italic);
                empty.gameObject.AddComponent<LayoutElement>().preferredHeight = 28f;
                ClearDetails();
                SetActionButtonsEnabled(false);
                return;
            }

            foreach (var m in _backups)
                AddBackupRow(m);

            if (string.IsNullOrEmpty(_selectedId) ||
                !_backups.Exists(b => string.Equals(b.id, _selectedId, StringComparison.OrdinalIgnoreCase)))
            {
                SelectBackup(_backups[0].id);
            }
            else
            {
                ShowDetails(_selectedId);
            }
        }

        private void AddBackupRow(MapBackupManifest m)
        {
            var rowGo = new GameObject($"Row_{m.id}", typeof(RectTransform));
            rowGo.transform.SetParent(_listContent, false);
            rowGo.AddComponent<LayoutElement>().preferredHeight = 56f;

            var img = rowGo.AddComponent<Image>();
            img.color = (string.Equals(m.id, _selectedId, StringComparison.OrdinalIgnoreCase))
                ? RowBgActive : RowBg;
            var btn = rowGo.AddComponent<Button>();
            var c = btn.colors;
            c.normalColor      = img.color;
            c.highlightedColor = RowBgHover;
            c.pressedColor     = RowBgActive;
            c.selectedColor    = img.color;
            btn.colors = c;
            btn.targetGraphic = img;
            string capturedId = m.id;
            btn.onClick.AddListener(() => SelectBackup(capturedId));

            // Slot + kind
            var top = AddText(rowGo.transform, $"{m.slot}  ·  {m.kind}",
                              13f, Accent, TextAlignmentOptions.Left, FontStyles.Bold);
            var topRt = top.rectTransform;
            topRt.anchorMin = new Vector2(0f, 1f); topRt.anchorMax = new Vector2(1f, 1f);
            topRt.pivot = new Vector2(0f, 1f);
            topRt.anchoredPosition = new Vector2(10f, -8f);
            topRt.sizeDelta = new Vector2(-20f, 18f);

            // Timestamp + size
            string when = DateTimeOffset.FromUnixTimeSeconds(m.createdUnixSeconds).LocalDateTime
                          .ToString("yyyy-MM-dd  HH:mm:ss");
            var sub = AddText(rowGo.transform,
                              $"{when}    {MapBackupStore.FormatBytes(m.totalBytes)}  ·  {m.fileCount} files",
                              11f, TextPrimary, TextAlignmentOptions.Left, FontStyles.Normal);
            var subRt = sub.rectTransform;
            subRt.anchorMin = new Vector2(0f, 0f); subRt.anchorMax = new Vector2(1f, 0f);
            subRt.pivot = new Vector2(0f, 0f);
            subRt.anchoredPosition = new Vector2(10f, 8f);
            subRt.sizeDelta = new Vector2(-20f, 16f);
        }

        private void SelectBackup(string id)
        {
            _selectedId = id;
            // Recolor existing rows without rebuilding the list.
            for (int i = 0; i < _listContent.childCount; i++)
            {
                var child = _listContent.GetChild(i);
                if (child == null) continue;
                var img = child.GetComponent<Image>();
                if (img == null) continue;
                bool sel = child.gameObject.name == $"Row_{id}";
                img.color = sel ? RowBgActive : RowBg;
            }
            ShowDetails(id);
        }

        // ── Details ──────────────────────────────────────────────────────────────

        private void ShowDetails(string id)
        {
            var m = _backups.Find(b => string.Equals(b.id, id, StringComparison.OrdinalIgnoreCase));
            if (m == null) { ClearDetails(); SetActionButtonsEnabled(false); return; }

            _detailHeader.text = m.id;
            string when = DateTimeOffset.FromUnixTimeSeconds(m.createdUnixSeconds).LocalDateTime
                          .ToString("yyyy-MM-dd HH:mm:ss");
            _detailBody.text =
                $"<b>Slot:</b> {m.slot}\n" +
                $"<b>Kind:</b> {m.kind}\n" +
                $"<b>Created:</b> {when}\n" +
                $"<b>Size:</b> {MapBackupStore.FormatBytes(m.totalBytes)}\n" +
                $"<b>Files:</b> {m.fileCount}\n" +
                $"<b>Label:</b> {m.label}";

            for (int i = _detailFilesContent.childCount - 1; i >= 0; i--)
                Destroy(_detailFilesContent.GetChild(i).gameObject);

            foreach (var rel in m.files)
            {
                var t = AddText(_detailFilesContent, rel, 11f, TextPrimary, TextAlignmentOptions.Left, FontStyles.Normal);
                t.gameObject.AddComponent<LayoutElement>().preferredHeight = 16f;
            }
            SetActionButtonsEnabled(true);
        }

        private void ClearDetails()
        {
            if (_detailHeader != null) _detailHeader.text = "Select a backup";
            if (_detailBody != null)   _detailBody.text   = "";
            if (_detailFilesContent != null)
                for (int i = _detailFilesContent.childCount - 1; i >= 0; i--)
                    Destroy(_detailFilesContent.GetChild(i).gameObject);
        }

        private void SetActionButtonsEnabled(bool enabled)
        {
            if (_restoreBtn != null) _restoreBtn.interactable = enabled;
            if (_deleteBtn != null)  _deleteBtn.interactable  = enabled;
        }

        // ── Restore ──────────────────────────────────────────────────────────────

        private void OnRestoreClicked()
        {
            if (string.IsNullOrEmpty(_selectedId)) return;
            // Always snap a safety backup before overwriting on-disk content.
            _store.CreateSnapshot(GuessActiveSlot(), "Pre-restore safety snapshot",
                                  MapBackupSchema.KindAutoBeforeRestore);
            bool ok = _store.RestoreBackup(_selectedId);
            SetStatus(ok
                ? $"Restored '{_selectedId}'. Reload the Map Editor to see the changes."
                : $"Restore failed for '{_selectedId}' — see console.");
            RefreshList();
        }
    }
}
