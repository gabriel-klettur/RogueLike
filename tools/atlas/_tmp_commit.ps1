Set-StrictMode -Off
$root = 'D:/Python/RogueLike'
function git(@(,$a)[string[]]$args) {
  & git.exe -C $root -c core.longpaths=true @args *>&1
}
function stamp($msg) {
  Write-Output "`n=== $msg ==="
}

# ---- Commit B: tools/atlas generated artifacts -----------------------------
stamp "Commit B: staging tools/atlas/generated pipeline artifacts"
git @('add', 'tools/atlas/generated/building_props_manifest_trees.json',
  'tools/atlas/generated/building_props_metadata_trees.json',
  'tools/atlas/generated/downloads_others_rename_map.json',
  'tools/atlas/generated/downloads_others_contact.png')
git @('commit', '-q', '-F', 'D:/temp/Rogue_commit_msgs/msg_B.txt')
stamp (git @('log', '-1', '--oneline'))

# ---- Commit A: imported tree sprites + templates + catalog + atlas ---------
stamp "Commit A: staging imported tree assets + BuildingTemplateData + catalog"
git @('add',
  'unity/Valkur/Assets/_Project/Resources/Buildings/nature/tree_*',
  'unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingTemplate_*',
  'unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset',
  'unity/Valkur/Assets/_Project/SpriteAtlases/buildings.spriteatlas')
git @('commit', '-q', '-F', 'D:/temp/Rogue_commit_msgs/msg_A.txt')
stamp (git @('log', '-1', '--oneline'))

# ---- Commit C: remaining session churn -------------------------------------
stamp "Commit C: staging remaining session churn"
git @('add', '-A')
git @('commit', '-q', '-F', 'D:/temp/Rogue_commit_msgs/msg_C.txt')
stamp (git @('log', '-1', '--oneline'))

Write-Output "`n=== FINAL STATUS ==="
git @('status', '--short') | Select-Object -First 15