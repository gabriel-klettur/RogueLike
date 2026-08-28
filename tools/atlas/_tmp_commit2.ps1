Set-StrictMode -Off
$root = 'D:/Python/RogueLike'

function Run-Git([string[]]$a) { & git.exe -C $root -c core.longpaths=true @a 2>&1 }

Write-Output "=== Commit B: tools/atlas/generated pipeline artifacts ==="
Run-Git @('add','tools/atlas/generated/building_props_manifest_trees.json','tools/atlas/generated/building_props_metadata_trees.json','tools/atlas/generated/downloads_others_rename_map.json','tools/atlas/generated/downloads_others_contact.png')
Run-Git @('commit','-q','-F','D:/temp/Rogue_commit_msgs/msg_B.txt')
Run-Git @('log','-1','--oneline')

Write-Output "`n=== Commit A: tree sprites + templates + catalog + atlas ==="
Run-Git @('add','unity/Valkur/Assets/_Project/Resources/Buildings/nature/tree_*','unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingTemplate_*','unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset','unity/Valkur/Assets/_Project/SpriteAtlases/buildings.spriteatlas')
Run-Git @('commit','-q','-F','D:/temp/Rogue_commit_msgs/msg_A.txt')
Run-Git @('log','-1','--oneline')

Write-Output "`n=== Commit C: remaining session churn ==="
Run-Git @('add','-A')
Run-Git @('commit','-q','-F','D:/temp/Rogue_commit_msgs/msg_C.txt')
Run-Git @('log','-1','--oneline')

Write-Output "`n=== FINAL git status --short (first 15) ==="
Run-Git @('status','--short') | Select-Object -First 15