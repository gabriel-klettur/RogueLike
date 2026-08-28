$ErrorActionPreference = 'Stop'
$root = 'D:/Python/RogueLike'
function g([string[]]$a) { & git.exe -C $root -c core.longpaths=true @a }

Set-Content 'D:/temp/Rogue_commit_msgs/commit_progress.txt' 'start'

try {
  g @('add','unity/Valkur/Assets/_Project/Resources/Buildings/nature/tree_*','unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingTemplate_*','unity/Valkur/Assets/_Project/Data/Catalogs/Buildings/BuildingCatalog.asset','unity/Valkur/Assets/_Project/SpriteAtlases/buildings.spriteatlas')
  g @('commit','-q','-F','D:/temp/Rogue_commit_msgs/msg_A.txt')
  $oneline = g @('log','-1','--oneline')
  Add-Content 'D:/temp/Rogue_commit_msgs/commit_progress.txt' ('A_ONELINE: ' + ($oneline -join ' '))
} catch {
  Add-Content 'D:/temp/Rogue_commit_msgs/commit_progress.txt' ('A_ERROR: ' + $_.Exception.Message)
}

try {
  g @('add','-A')
  g @('commit','-q','-F','D:/temp/Rogue_commit_msgs/msg_C.txt')
} catch {
  Add-Content 'D:/temp/Rogue_commit_msgs/commit_progress.txt' ('C_ERROR: ' + $_.Exception.Message)
}

$onelineC = g @('log','-1','--oneline')
Add-Content 'D:/temp/Rogue_commit_msgs/commit_progress.txt' ('C_ONELINE: ' + ($onelineC -join ' '))
Add-Content 'D:/temp/Rogue_commit_msgs/commit_progress.txt' 'END'