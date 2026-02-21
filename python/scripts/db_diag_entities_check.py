from __future__ import annotations

import sqlite3
from pathlib import Path

DB_PATH = Path("data/roguelike.sqlite3")


def main() -> None:
    conn = sqlite3.connect(DB_PATH)
    c = conn.cursor()

    cols = [r[1] for r in c.execute("PRAGMA table_info(entities)").fetchall()]
    print("cols_count:", len(cols))
    print("cols_sample:", cols[:20], "...", cols[-20:])

    def row(sql: str) -> tuple | None:
        try:
            return c.execute(sql).fetchone()
        except Exception as e:
            print("query_error:", e)
            return None

    print("barbol_boss:", row(
        "SELECT id, kind, hp, speed, faction, aggro_range, patrol_id "
        "FROM entities WHERE id='barbol_boss'"
    ))
    print("barbol_baby:", row(
        "SELECT id, kind, hp, speed, patrol_id, patrol_segments, patrol_step_tiles, patrol_amplitude_tiles, patrol_axis "
        "FROM entities WHERE id='barbol_baby'"
    ))
    print("vendor_alchemist_valeria:", row(
        "SELECT id, kind, chat_range, ai_behavior FROM entities WHERE id='vendor_alchemist_valeria'"
    ))
    print("dwarf:", row(
        "SELECT id, kind, basic_speed, basic_attack, basic_armor, trail_interval, trail_life_time, trail_max_trails "
        "FROM entities WHERE id='dwarf'"
    ))

    # One asset path sample (now from child table entities_assets_no_set)
    print(
        "barbol_boss_assets:",
        row(
            "SELECT "
            "MAX(CASE WHEN action='idle' AND direction='s' THEN path END), "
            "MAX(CASE WHEN action='walk' AND direction='e' THEN path END), "
            "MAX(CASE WHEN action='death' AND direction='s' THEN path END) "
            "FROM entities_assets_no_set WHERE entity_id='barbol_boss'"
        ),
    )

    # New assets tables counts and samples
    try:
        print("sets_count:", c.execute("SELECT COUNT(*) FROM entities_assets_set").fetchone())
        print("no_sets_count:", c.execute("SELECT COUNT(*) FROM entities_assets_no_set").fetchone())
        print(
            "sample_set:",
            c.execute(
                "SELECT entity_id, action, idx, path FROM entities_assets_set ORDER BY entity_id, action, idx LIMIT 5"
            ).fetchall(),
        )
        print(
            "sample_no_set:",
            c.execute(
                "SELECT entity_id, action, direction, path FROM entities_assets_no_set ORDER BY entity_id, action, direction LIMIT 5"
            ).fetchall(),
        )
    except Exception as e:
        print("assets_tables_query_error:", e)

    # Verify payload archive coverage and integrity (only if entities.extra_json exists)
    try:
        has_extra = "extra_json" in cols
        cnt_archive = c.execute("SELECT COUNT(*) FROM entities_payload_archive").fetchone()[0]
        if has_extra:
            cnt_entities_with_json = c.execute(
                "SELECT COUNT(*) FROM entities WHERE extra_json IS NOT NULL"
            ).fetchone()[0]
        else:
            cnt_entities_with_json = None
        print("payload_archive_count:", cnt_archive, "entities_with_json:", cnt_entities_with_json)

        if has_extra:
            # Join by entity and compare hashes when both sides have payload
            c.execute("DROP TABLE IF EXISTS _tmp_cmp")
            c.execute(
                "CREATE TEMP TABLE _tmp_cmp AS "
                "SELECT e.id AS eid, e.extra_json AS ej, a.content_hash AS ah FROM entities e "
                "LEFT JOIN entities_payload_archive a ON a.entity_id = e.id"
            )
            import hashlib
            mismatches = 0
            missing = 0
            rows = c.execute("SELECT eid, ej, ah FROM _tmp_cmp").fetchall()
            for eid, ej, ah in rows:
                if ej is None:
                    continue
                try:
                    h = hashlib.sha256(ej.encode("utf-8")).hexdigest()
                except Exception:
                    h = None
                if ah is None:
                    missing += 1
                elif h != ah:
                    mismatches += 1
            print("payload_archive_missing:", missing, "payload_archive_mismatch:", mismatches)
    except Exception as e:
        print("payload_archive_error:", e)

    conn.close()


if __name__ == "__main__":
    main()
