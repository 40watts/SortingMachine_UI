# Hygiene Git et secrets

## Regle

Le depot ne doit contenir que le code source, la documentation, les scripts de build/test et les assets necessaires a la compilation.

Les donnees runtime, exports atelier, caches WebView2, executables generes et secrets Odoo restent hors Git.

## Secret Odoo

La configuration Odoo locale doit etre stockee hors depot, par exemple:

```text
%APPDATA%\TriCellPilot\ODOO.txt
```

Formats acceptes:

```text
url=https://40-watts-cycles.odoo.com
api_key=...
```

ou variables d'environnement:

```text
ODOO_URL
ODOO_API_KEY
```

Ne jamais placer `ODOO.txt`, `.env`, `odoo_config.json` ou une cle API dans `SortingMachine_UI`.

## Artefacts exclus avant premier commit

- `desktop_v2/bin/`
- `desktop_app/bin/`
- `webview2_pkg/`
- `webview2.zip`
- `webview2.nupkg`
- `desktop_v2/app/data/`
- `runtime_trace.csv`, `history.csv`, `observation.csv`, `odoo_cell_tests.csv`
- backups `*.bak*` et fichiers `*.corrupt_*`

## Controle pre-commit local

Avant initialisation Git ou commit, lancer:

```bat
powershell -NoProfile -ExecutionPolicy Bypass -File desktop_v2\tools\RepositoryPreflight.ps1 -Root .
```
