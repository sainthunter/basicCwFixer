# Process Migration Analyzer CLI

Streaming validator for ConceptWave Process version references. It scans huge XML files without loading into memory and only validates the latest version of each process family.

## Usage

```bash
dotnet run --project webBasicCWFixer.ProcessMigration -- \
  --input /path/to/ConceptWaveMetadata.xml \
  --output /path/to/findings.csv \
  --format csv
```

JSONL output:

```bash
dotnet run --project webBasicCWFixer.ProcessMigration -- \
  --input /path/to/ConceptWaveMetadata.xml \
  --output /path/to/findings.jsonl \
  --format jsonl
```

Debug stats:

```bash
dotnet run --project webBasicCWFixer.ProcessMigration -- \
  --input /path/to/ConceptWaveMetadata.xml \
  --output /path/to/findings.csv \
  --debug
```

## CSV Headers

```
parentProcessName,parentBaseName,parentVersion,refType,targetProcessName,targetBaseName,referencedVersion,expectedVersion,severity,location,reason
```

## Notes

- Only latest versions of each process family are validated.
- Output is streamed to keep memory usage low.
- Version parsing supports `_v_1_12` and `_v1_18` formats.
