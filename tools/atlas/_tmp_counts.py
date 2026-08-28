import subprocess,sys,os
root = 'D:/Python/RogueLike'
cfg = ['-C',root,'-c','core.longpaths=true','-c','core.autocrlf=false','-c','core.eol=lf']
def g(*a,**k):
    kw = dict(capture_output=True,text=True,timeout=k.pop('timeout',600))
    r = subprocess.run(['git.exe',*cfg,*a],**kw)
    return r.stdout.rstrip(), r.stderr.rstrip(), r.returncode

msgC = """chore: commit session working-tree churn (non-tree)

Commits unrelated working-tree modifications present before the tree-import task:

- 12 FSM editor C# files (FSMRuntimeEditor.*.cs, FSMEditorUIBuilder.Panels.cs):
  mid-flight FSM editor dev; compiles clean (editor console empty after refresh).
- StreamingAssets/*.json (Buildings/*, FSM/*, Particles/*): runtime world-state
  written via the IRepository pattern; .bak sidecar is gitignored.
- ProjectSettings/Physics2DSettings.asset; dwarf.asset; MainGameplay.unity.
"""
with open(os.path.join(root,'_msg_C.txt'),'w',encoding='utf-8') as f:
    f.write(msgC)

o,er,rc = g('add','-A')
print("add -A:", "OK" if not er else er[:150])
o,er,rc = g('commit','-q','-F','_msg_C.txt')
print("commit C:", "OK" if rc==0 else ("ERR rc=%d %s"%(rc,er[:150])))
print("oneline:", g('log','-1','--oneline')[0])
# cleanup
try: os.remove(os.path.join(root,'_msg_C.txt'))
except: pass
sys.stdout.flush()
