# Release workflow gating (simulated from .github/workflows/release.yml)

tag filter: ['v*.*.*']
gated steps (`if: startsWith(github.ref, 'refs/tags/v')`): ['Push to NuGet', 'Create GitHub Release']

| trigger | ref_name | workflow runs | publishes (NuGet + GH release) | prerelease | makeLatest |
| --- | --- | --- | --- | --- | --- |
| tag push | `v2.0.0-preview.1` | yes | yes | yes | no |
| tag push | `v2.0.0` | yes | yes | no | yes |
| tag push | `v2.0.1` | yes | yes | no | yes |
| tag push | `docs-2025` | no | no | - | - |
| tag push | `v2.0` | no | no | - | - |
| workflow_dispatch | `fm/vexel-v2-t15` | yes | no | - | - |

# build.yml jobs
- `build`: needs=none, matrix=None, steps=['actions/checkout@v4', 'Setup .NET', 'Restore dependencies', 'Verify formatting', 'Build', 'Pack (dry-run)', 'Assert pack layout']
- `test`: needs=none, matrix={'tfm': ['net10.0', 'net11.0']}, steps=['actions/checkout@v4', 'Setup .NET', 'Restore dependencies', 'Build', 'Test (${{ matrix.tfm }})']
