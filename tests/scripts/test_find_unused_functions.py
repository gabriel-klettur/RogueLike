import subprocess
import sys
import os
import pytest

def test_find_unused_functions_detects_unused(tmp_path):
    # Create a module with used and unused functions
    d = tmp_path / "mod"
    d.mkdir()
    f = d / "a.py"
    f.write_text("""def used(): pass

def unused(): pass
used()
""", encoding="utf-8")
    # Run the script
    script = os.path.abspath(os.path.join(os.path.dirname(__file__), os.pardir, os.pardir, 'scripts', 'find_unused_functions.py'))
    result = subprocess.run([sys.executable, script, str(d)], capture_output=True, text=True)
    assert 'unused' in result.stdout
    assert '-> used' not in result.stdout
