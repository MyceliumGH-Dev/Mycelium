# TemplateSync.Cli

Compares the `.ghx` templates in
[Mycelium-Templates](https://github.com/MyceliumGH-Dev/Mycelium-Templates) against the
component definitions in `src/Mycelium`, and optionally repairs what can be repaired by
editing the archive.

## Why

The **Mycelium Templates** component hands the user whatever definition it finds on the
branch matching their installed version. Nothing at run time checks that the components in
that definition still exist, or still have the ports the plug-in registers — so a stale
template fails silently on the user's canvas.

Two failure modes, in order of severity:

| Problem | Effect | Fixable here |
| --- | --- | --- |
| Archived component GUID matches no component | Grasshopper cannot instantiate it; the user gets an unresolved placeholder | Yes, when the archived display name identifies exactly one component |
| Port `Name`/`NickName` drifted from the registration | Labels on the canvas disagree with the docs and the component's own tooltips | Yes |
| Port **count** differs from the registration | Wires land on the wrong ports, or an input the plug-in expects is absent | **No** — see below |

Port counts cannot be corrected by editing XML: adding a port means producing a
fully-formed parameter chunk with a type, access mode, and persistent data. That is a
re-save in Grasshopper, so the tool reports it and stops.

## Usage

```bash
dotnet run --project tools/TemplateSync.Cli
```

Report and fix:

```bash
dotnet run --project tools/TemplateSync.Cli -- --fix
```

| Option | Meaning |
| --- | --- |
| `--repo-dir <path>` | Local Mycelium-Templates checkout. Cloned if missing. Default: `$MYCELIUM_TEMPLATE_REPO_DIR`, else `~/Documents/GitHub/MyceliumGH-Dev/Mycelium-Templates` |
| `--branch <name>` | Branch to check. Default: the version in `manifest.yml` |
| `--no-git-update` | Do not fetch or checkout; use the working tree as it is |
| `--fix` | Rewrite in place what can be rewritten |
| `-h`, `--help` | Usage |

Exit codes: `0` clean, `1` outstanding findings, `2` error. `--fix` runs exit `0` when
everything it found was repaired, so the same command works as a CI gate.

If the branch named after the current version does not exist yet — the normal state before
a release, and what `template-branch-sync.yml` creates — the tool warns and checks `main`
instead, which is what users on that version see anyway.

## No Rhino required

Component definitions are parsed out of the C# **source** (`ComponentGuid`, the
`: base(name, nickname, …)` call, and the `pManager.Add*Parameter` calls in
`RegisterInputParams`/`RegisterOutputParams`, following the base class when a component
inherits them — every `*Config` component does). Nothing reflects over a built `.gha`, so
the tool needs neither Rhino nor Windows and runs in Linux CI.

The same parsing backs `tests/Mycelium.Templates.Tests`, which is what
`.github/workflows/template-integrity.yml` runs.

## Known drift as of 0.1.0.4

Running this against the current template repo is not clean, and the findings are real:

- **`main` only**: the Massing Generator in both templates is archived under the
  *assembly* GUID `20543b24-…` rather than its own `8dd5a26c-…` — the collision fixed in
  plug-in `0.1.0.0`. Anyone whose version has no matching branch falls back to `main` and
  gets a definition whose main component does not load. `--fix` repairs it.
- **All branches**: every port's `NickName` is archived as the full parameter name
  (`"TreeDensity"` rather than `"TDens"`), inherited from definitions authored before the
  rename. Cosmetic, and `--fix` repairs it.
- **All branches**: the Massing Generator has 10 archived inputs against 11 registered —
  the `StreetNetwork` input added after the templates were last saved. This one needs a
  Grasshopper re-save.
