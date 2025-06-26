## **Hoja: INFO**

| Project | Client | Owner | Date | Version | Notes |
| ------- | ------ | ----- | ---- | ------- | ----- |
| RogueLike | Internal | Encargado de Sonido | 2025-06-05 | 1.0 | Especificación inicial de audio |

---

## **Hoja: SFX (Sound Effects)**

| ID | Name | File Name | Category | Tags | Loop | Volume | Length (sec) | Format | Bitrate (kbps) | Notes |
| -- | ---- | --------- | -------- | ---- | ---- | ------ | ------------ | ------ | -------------- | ----- |
| SFX_001 | Player Hit     | player_hit.wav      | Player    | hit,damage       | FALSE | 0.7 | 0.5 | wav | 256 | Sonido al recibir daño |
| SFX_002 | Enemy Hit      | enemy_hit.wav       | Enemy     | hit,damage       | FALSE | 0.7 | 0.5 | wav | 256 | Sonido al enemigo al ser golpeado |
| SFX_003 | Enemy Death    | enemy_die.wav       | Enemy     | death            | FALSE | 0.8 | 1.0 | wav | 256 | Sonido al morir el enemigo |
| SFX_004 | Spell Cast     | spell_cast.wav      | Spell     | cast,magic       | FALSE | 0.7 | 0.6 | wav | 256 | Sonido al lanzar hechizo |
| SFX_005 | Item Pickup    | pickup_item.wav     | System    | pickup           | FALSE | 0.6 | 0.4 | wav | 256 | Sonido al recoger un objeto |
| SFX_006 | Fireball Launch  | fireball_launch.wav  | Spell     | fireball,launch    | FALSE | 0.8 | 0.5 | wav | 192 | Lanzamiento de fireball |
| SFX_007 | Fireball Impact  | fireball_impact.wav  | Spell     | fireball,impact    | FALSE | 1.0 | 0.8 | wav | 256 | Impacto de fireball |
| SFX_008 | Iceball Launch   | iceball_launch.wav   | Spell     | iceball,launch     | FALSE | 0.8 | 0.5 | wav | 192 | Lanzamiento de iceball |
| SFX_009 | Iceball Impact   | iceball_impact.wav   | Spell     | iceball,impact     | FALSE | 1.0 | 0.8 | wav | 256 | Impacto de iceball |
| SFX_010 | Lightball Launch | lightball_launch.wav | Spell     | lightball,launch   | FALSE | 0.8 | 0.5 | wav | 192 | Lanzamiento de lightball |
| SFX_011 | Lightball Impact | lightball_impact.wav | Spell     | lightball,impact   | FALSE | 1.0 | 0.8 | wav | 256 | Impacto de lightball |
| SFX_012 | Darkball Launch  | darkball_launch.wav  | Spell     | darkball,launch    | FALSE | 0.8 | 0.5 | wav | 192 | Lanzamiento de darkball |
| SFX_013 | Darkball Impact  | darkball_impact.wav  | Spell     | darkball,impact    | FALSE | 1.0 | 0.8 | wav | 256 | Impacto de darkball |
| SFX_014 | Healing Aura Start| healing_aura_start.wav| Spell   | healing,aura       | FALSE | 0.6 | 0.5 | wav | 256 | Activación de aura de curación |
| SFX_015 | Healing Aura Tick | healing_aura_tick.wav | Spell   | healing,aura,tick  | FALSE | 0.4 | 0.3 | wav | 128 | Tick de curación de aura |
| SFX_016 | Laser Beam Start | laser_beam_start.wav  | Spell     | laser_beam,start   | FALSE | 0.7 | 0.6 | wav | 256 | Inicio de rayo láser |
| SFX_017 | Laser Beam Loop  | laser_beam_loop.wav   | Spell     | laser_beam,loop    | TRUE  | 0.5 | 2.0 | wav | 128 | Loop de rayo láser |
| SFX_018 | Laser Beam Hit   | laser_beam_hit.wav    | Spell     | laser_beam,hit     | FALSE | 0.9 | 0.7 | wav | 256 | Impacto de rayo láser |
| SFX_019 | Dash Whoosh      | dash_whoosh.wav       | Spell     | dash,movement      | FALSE | 0.5 | 0.3 | wav | 128 | Sonido de dash |
| SFX_020 | Slash Attack     | slash_attack.wav      | Spell     | slash,attack       | FALSE | 0.7 | 0.5 | wav | 256 | Sonido de ataque slash |
| SFX_021 | Explosion        | explosion.wav         | Effect    | explosion          | FALSE | 1.0 | 1.2 | wav | 320 | Sonido de explosión |
| SFX_022 | Barbol Idle Loop       | barbol_idle_loop.wav         | NPC         | idle                | TRUE  | 0.3  | 10.0    | wav | 192 | Loop ambiente idle de Barbol |
| SFX_023 | Barbol Patrol Step     | barbol_patrol_step.wav       | NPC         | patrol,footstep     | TRUE  | 0.5  | 1.0     | wav | 192 | Pasos de Barbol patrullando |
| SFX_024 | Barbol Alert           | barbol_alert.wav             | NPC         | alert,roar          | FALSE | 1.0  | 1.2     | wav | 256 | Sonido de alerta de Barbol al detectar jugador |
| SFX_025 | Barbol Chase Step      | barbol_chase_step.wav        | NPC         | chase,footstep      | TRUE  | 0.6  | 1.0     | wav | 192 | Pasos de Barbol persiguiendo |
| SFX_026 | Barbol Attack Growl    | barbol_attack_growl.wav      | NPC         | attack,growl        | FALSE | 1.0  | 0.8     | wav | 256 | Gruñido de ataque de Barbol |
| SFX_027 | Barbol Damage          | barbol_damage.wav            | NPC         | damage              | FALSE | 0.9  | 0.5     | wav | 256 | Sonido al recibir daño |
| SFX_028 | Barbol Death Roar      | barbol_death_roar.wav        | NPC         | death,roar          | FALSE | 1.0  | 1.5     | wav | 256 | Sonido de muerte de Barbol |
| SFX_029 | Barbol Flee            | barbol_flee.wav              | NPC         | flee                | TRUE  | 0.5  | 5.0     | wav | 192 | Barbol huyendo |

---

## **Hoja: UI (User Interface Sounds)**

| ID | Name | File Name | Trigger | Tags | Loop | Volume | Length (sec) | Format | Bitrate (kbps) | Notes |
| -- | ---- | --------- | ------- | ---- | ---- | ------ | ------------ | ------ | -------------- | ----- |
| UI_001  | Click           | ui_click.wav        | ButtonPress | ui,click       | FALSE | 0.5 | 0.1 | wav | 128 | Sonido al hacer click |
| UI_002  | Hover           | ui_hover.wav        | ButtonHover | ui,hover       | FALSE | 0.5 | 0.1 | wav | 128 | Sonido al pasar cursor |
| UI_003  | Open Menu       | ui_open_menu.wav    | MenuOpen    | ui,menu        | FALSE | 0.6 | 0.8 | wav | 128 | Sonido al abrir menú |
| UI_004  | Close Menu      | ui_close_menu.wav   | MenuClose   | ui,menu        | FALSE | 0.6 | 0.8 | wav | 128 | Sonido al cerrar menú |

---

## **Hoja: MUSIC**

| ID | Track Title | File Name | Composer | Mood | Loop | Tags | Length (sec) | Format | Bitrate (kbps) | Notes |
| -- | ----------- | --------- | -------- | ---- | ---- | ---- | ------------ | ------ | -------------- | ----- |
| MUS_001 | Main Theme     | main_theme.ogg      | TBD       | Epic            | TRUE  | ambient,theme | 120 | ogg | 192 | Música principal del juego |
| MUS_002 | Dungeon Theme  | dungeon_theme.ogg   | TBD       | Tense           | TRUE  | ambient,dungeon | 90  | ogg | 192 | Música de mazmorra |
| MUS_003 | Boss Theme     | boss_theme.ogg      | TBD       | Intense         | TRUE  | boss,battle | 100 | ogg | 192 | Música de jefe |
| MUS_004 | Map Edit Theme | map_edit_theme.ogg  | TBD       | Calm            | TRUE  | editing,map | 180 | ogg | 192 | Música para modo edición de mapas |
| MUS_005 | Tile Edit Theme| tile_edit_theme.ogg | TBD       | Focused         | TRUE  | editing,tile| 150 | ogg | 192 | Música para modo edición de tiles |
| MUS_006 | Building Edit Theme | building_edit_theme.ogg | TBD | Creative       | TRUE  | editing,building | 160 | ogg | 192 | Música para modo edición de edificios |
---

## **Hoja: DIALOGUE**

| ID | Character | Dialogue Line | File Name | Language | Emotion | Tags | Length (sec) | Format | Bitrate (kbps) | Notes |
| -- | --------- | ------------- | --------- | -------- | ------- | ---- | ------------ | ------ | -------------- | ----- |
| DLG_001 | Narrador       | Bienvenido al calabozo | narracion_intro.wav | ES | Neutral | narration,intro | 5 | wav | 256 | Narración de introducción |
| DLG_002 | Enemigo        | ¡Te destruiré!        | enemy_taunt.wav     | ES | Aggressive | taunt,enemy   | 2 | wav | 256 | Frase de taunt de enemigo |
