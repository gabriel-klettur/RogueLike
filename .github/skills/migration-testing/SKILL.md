---
name: migration-testing
description: "Test and validate migration quality between Python and Unity. Use when running parity checks, writing Unity tests, comparing Python/Unity behavior, validating data integrity, performance benchmarking. Covers pytest, NUnit, data validation, and regression testing."
argument-hint: "Describe what to test (system parity, data integrity, performance, etc.)"
---

# Migration Testing & Validation

## When to Use
- Verifying a ported system matches Python behavior
- Writing new Unity tests for migrated features
- Running data migration validation (dry-run)
- Comparing performance metrics (Python vs Unity)
- Finding regressions after changes

## Testing Infrastructure

### Python Test Suite
- **Location**: `python/tests/`
- **Framework**: pytest
- **Config**: `python/pytest.ini`
- **Run**: `cd python && python -m pytest tests/ -v`
- **Key fixtures** (`conftest.py`):
  - Headless Pygame initialization
  - `FakeWorld` — mock ECS world
  - `FakeCamera` — mock camera
  - `RL_DATA_DIR` env var for test data isolation

### Unity Test Suite
- **Location**: `unity/Valkur/Assets/Tests/`
- **Framework**: Unity Test Framework (NUnit)
- **Assemblies**: `Valkur.Tests.EditMode`, `Valkur.Tests.PlayMode`
- **Run**: Unity Test Runner window or CLI

### Data Validation Tools
- `PythonDataMigrator.cs` — dry-run mode (validates without writing)
- `ContentValidator.cs` — asset reference validation
- `BuildValidator.cs` — build-time validation
- Menu: `Valkur > Migration > Dry-Run All (Validate Only)`

## Procedure: System Parity Check

### 1. Identify the System to Test
Read both implementations:
- Python: `python/src/roguelike_game/ecs/systems/[system]/`
- Unity: `unity/Valkur/Assets/_Project/Scripts/Gameplay/[system]/`

### 2. Extract Test Cases from Python
Read `python/tests/test_[system].py`:
- Input conditions
- Expected outputs
- Edge cases tested

### 3. Write/Verify Unity Tests
Create NUnit test class in `Assets/Tests/EditMode/` or `Assets/Tests/PlayMode/`:

```csharp
using NUnit.Framework;

[TestFixture]
public class [System]ParityTests
{
    [Test]
    public void DamageFormula_MatchesPython()
    {
        // Arrange: same input as Python test
        // Act: run Unity code
        // Assert: same output as Python
    }
}
```

### 4. Compare Numerical Values
For each test case, verify:
- [ ] Same input produces same output
- [ ] Same damage values
- [ ] Same timing (within float tolerance)
- [ ] Same state transitions
- [ ] Same edge case handling

### 5. Write Parity Report

```markdown
## Parity Report: [System Name]
Date: [date]
Python ref: [file path]
Unity impl: [file path]

### Status: ✅ PASS / ⚠️ PARTIAL / ❌ FAIL

### Test Results
| Test Case | Python Result | Unity Result | Match |
|-----------|---------------|--------------|-------|

### Value Comparison
| Value | Python | Unity | Delta | Acceptable |
|-------|--------|-------|-------|-----------|

### Missing Features
- [ ] [feature not yet ported]
```

## Procedure: Data Integrity Validation

### 1. Run Python Data Audit
```bash
cd python
python -c "import json; data = json.load(open('data/entities/new_hostiles.json')); print(f'Hostiles: {len(data[\"hostiles\"][\"classes\"])} classes')"
```

### 2. Run Unity Dry-Run
Use menu: `Valkur > Migration > Dry-Run All (Validate Only)`

### 3. Compare Counts
| Domain | Python Count | Unity Count | Match |
|--------|-------------|-------------|-------|
| Monster classes | N | N | ✅/❌ |
| Spells | N | N | ✅/❌ |
| Player classes | N | N | ✅/❌ |
| Items | N | N | ✅/❌ |

### 4. Spot-Check Values
Pick 3 random entities per domain and compare every field.

## Performance Benchmarking

### Python Metrics (from benchmarks)
Check `logs/benchmarks/benchmarks_run_*.json` for historical data.

### Unity Metrics
- `PerformanceMonitor.cs` (F3 overlay): FPS avg, p95, p99, GC count
- Unity Profiler: CPU, GPU, memory, draw calls

### Targets (from phase_00_baseline_and_parity.md)
| Metric | Python Baseline | Unity Target |
|--------|----------------|--------------|
| FPS avg | [read from benchmark] | ≥60 |
| Frame p95 | [read from benchmark] | <16.6ms |
| Memory | [read from benchmark] | ≤Python |
| Load time | [read from benchmark] | ≤Python |
