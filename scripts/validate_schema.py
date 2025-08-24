import json
from pathlib import Path
from jsonschema import Draft7Validator, RefResolver

base = Path(__file__).resolve().parents[1]
schema_path = base / 'data' / 'spells' / 'schema.json'
spells_path = base / 'data' / 'spells' / 'spells.json'

schema = json.loads(schema_path.read_text(encoding='utf-8'))
spells = json.loads(spells_path.read_text(encoding='utf-8'))

resolver = RefResolver.from_schema(schema)
validator = Draft7Validator(schema, resolver=resolver)
errors = sorted(validator.iter_errors(spells), key=lambda e: list(e.path))
print(f'Total errors: {len(errors)}')
for e in errors:
    path = "/".join(map(str, e.path))
    print(f'- path: {path or "<root>"}')
    print(f'  validator: {e.validator}')
    print(f'  message: {repr(e.message)}')
    # Show causing schema refs when present
    schema_path = e.schema_path
    print(f'  schema_path: {list(schema_path)}')
    if e.context:
        print('  contexts:')
        for sub in e.context:
            subpath = "/".join(map(str, sub.path))
            print(f'    - sub.validator: {sub.validator}')
            print(f'      sub.path: {subpath or "<root>"}')
            print(f'      sub.message: {repr(sub.message)}')

# Per-entry check: does each entry validate as legacy or new?
legacy_def = schema['definitions']['legacySpell']
new_def = schema['definitions']['newSpell']
legacy_validator = Draft7Validator(legacy_def)
new_validator = Draft7Validator(new_def)

print('\nPer-entry conformance:')
oneof_subschema = {"oneOf": [ {"$ref": "#/definitions/legacySpell"}, {"$ref": "#/definitions/newSpell"} ]}
sub_validator = Draft7Validator(oneof_subschema, resolver=resolver)
for key, val in spells.items():
    ok = sub_validator.is_valid(val)
    status = 'ok' if ok else 'fail'
    print(f'- {key}: {status}')
    if not ok:
        sub_errors = list(sub_validator.iter_errors(val))
        for se in sub_errors:
            print(f'    reason: {repr(se.message)}')
            if se.context:
                for ctx in se.context:
                    print(f'      ctx: {repr(ctx.message)}')
