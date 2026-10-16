#!/usr/bin/env python3
"""Portable source build and strictly allowlisted Thunderstore packaging (Python 3.9+)."""
import argparse
import glob
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent
LOCK = json.loads((HERE / 'xr-dependencies.lock.json').read_text())
XR = LOCK['packages']
NATIVE = LOCK['native_files']
XR_NAMES = [p['assembly'] for p in XR]
OUTPUTS = XR_NAMES + ['VRPatcher.dll', 'VRMod.dll', 'VRAPI.dll']
PACKAGE_METADATA = (
    'manifest.json', 'README.md', 'CHANGELOG.md', 'icon.png', 'LICENSE.md',
    'THIRD-PARTY-NOTICES.md', 'tools/restore-native.py', 'build-tools/README.md',
    'docs/STEAM-MACHINE.md', 'docs/FOVEATION.md', 'docs/UPSTREAM-ISSUES.md',
    'docs/VERIFICATION.md',
)
RESOURCES = {
    'VRPatcher': {
        'VREnabler/Plugins/openxr_loader.dll': 'VRPatcher.Plugins.openxr_loader.dll',
        'VREnabler/Plugins/UnityOpenXR.dll': 'VRPatcher.Plugins.UnityOpenXR.dll',
        'VREnabler/Dependencies/UnitySubsystemsManifest.json': 'VRPatcher.Dependencies.UnitySubsystemsManifest.json',
    },
    'VRMod': {'RoR2VRMod/Properties/vrmodassets': 'VRMod.Properties.vrmodassets'},
}
MONOMOD_FILES = ['Mono.Cecil.dll', 'MonoMod.Utils.dll', 'MonoMod.RuntimeDetour.dll',
                 'MonoMod.exe', 'MonoMod.RuntimeDetour.HookGen.exe']


def sha256(path):
    digest = hashlib.sha256()
    with Path(path).open('rb') as handle:
        for block in iter(lambda: handle.read(1024 * 1024), b''):
            digest.update(block)
    return digest.hexdigest()


def read_refs(name):
    return [line.strip() for line in (HERE / name).read_text().splitlines()
            if line.strip() and not line.lstrip().startswith('#')]


def variables(a):
    return {'GAME_MANAGED': str(a.game_managed), 'REFS': str(a.refs),
            'BUILD': str(a.build_dir), 'BEPINEX_CORE': str(a.bepinex_core)}


def expand(value, a):
    for key, path in variables(a).items():
        value = value.replace('${' + key + '}', path)
    return value


def public_inputs(a):
    """Publicize copies only; never mutate the installed game's Managed folder."""
    result = {}
    for entry in read_refs('vrmod_refs.txt'):
        if not entry.startswith('${REFS}/publicized/'):
            continue
        name = Path(entry).name
        if name in XR_NAMES:
            result[name] = a.build_dir / 'out' / name
        elif name in ('Bhaptics.Tact.dll', 'ShockwaveManager.dll'):
            result[name] = a.refs / 'optional' / name
        else:
            result[name] = a.game_managed / name
    return result


def package_dir(a, package):
    return a.packages / (package['name'] + '@' + package['version'])


def native_dependency_errors(a):
    errors = []
    for native in NATIVE:
        dependency = next(item for item in XR if item['name'] == native['package'])
        local = ROOT / native['resource']
        source = package_dir(a, dependency) / native['source']
        for path in (local, source):
            if not path.is_file():
                errors.append(f'Missing locked native DLL: {path}')
            elif sha256(path) != native['sha256']:
                errors.append(f'Native DLL differs from locked {dependency["name"]}@{dependency["version"]}: {path}')
    return errors


def reference_evidence(a):
    evidence = {'kind': a.reference_source, 'game_runtime_verified': False}
    if a.reference_source == 'reference-feed':
        if a.reference_provenance is None or not a.reference_provenance.is_file():
            raise ValueError('Reference-feed builds require --reference-provenance JSON with source URLs and DLL hashes.')
        manifest = json.loads(a.reference_provenance.read_text())
        if manifest.get('schema') != 1 or manifest.get('kind') != 'public-reference-feed' or not manifest.get('sources'):
            raise ValueError('Invalid reference-feed provenance schema/kind/sources.')
        actual = {path.name: sha256(path) for path in a.game_managed.glob('*.dll')}
        if not actual or manifest.get('files') != actual:
            raise ValueError('Reference-feed DLLs differ from their provenance manifest; restore or update verified references.')
        evidence['provenance_sha256'] = sha256(a.reference_provenance)
        evidence['sources'] = manifest['sources']
    return evidence


def dependency_errors(a):
    errors = native_dependency_errors(a)
    try:
        reference_evidence(a)
    except (OSError, ValueError) as error:
        errors.append(str(error))
    paths = []
    for file in ('common_refs.txt', 'bepinex_refs.txt', 'vrmod_refs.txt'):
        for value in read_refs(file):
            if '${REFS}/publicized/' not in value and '${BUILD}/' not in value:
                pattern = expand(value, a)
                found = [Path(p) for p in glob.glob(pattern) if Path(p).is_file()]
                if not found:
                    errors.append(f'Missing reference: {pattern}')
                paths.extend(found)
    paths.extend(path for name, path in public_inputs(a).items() if name not in XR_NAMES)
    paths.extend(a.monomod / name for name in MONOMOD_FILES)
    paths.extend(ROOT / path for resources in RESOURCES.values() for path in resources)
    for path in paths:
        if not path.is_file() or path.stat().st_size == 0:
            errors.append(f'Missing or empty input: {path}')
    for package in XR:
        directory = package_dir(a, package)
        manifest = directory / 'package.json'
        for license_name in package['license_files']:
            if not (directory / license_name).is_file():
                errors.append(f'Missing XR license/notice: {directory / license_name}')
        if not manifest.is_file():
            errors.append(f'Missing XR source: {directory}; run fetch-xr')
            continue
        try:
            info = json.loads(manifest.read_text())
            if info.get('name') != package['name'] or info.get('version') != package['version']:
                errors.append(f'XR package identity/version mismatch: {manifest}')
            head = subprocess.check_output(['git', '-C', str(directory), 'rev-parse', 'HEAD'], text=True, stderr=subprocess.DEVNULL).strip()
            dirty = subprocess.check_output(['git', '-C', str(directory), 'status', '--porcelain', '--untracked-files=all'], text=True)
            if head != package['commit'] or dirty:
                errors.append(f'XR sources differ from lock (commit or local edits): {directory}')
            if not (directory / package['source']).is_dir():
                errors.append(f'Missing XR runtime source directory: {directory / package["source"]}')
        except (ValueError, OSError, subprocess.CalledProcessError) as exc:
            errors.append(f'Cannot verify locked XR checkout {directory}: {exc}')
    if not shutil.which(a.pwsh):
        errors.append(f'PowerShell 7 not found: {a.pwsh}; install it or set --pwsh PATH / PWSH')
    return sorted(set(errors))


def preflight(a):
    errors = dependency_errors(a)
    if errors:
        raise ValueError('Preflight failed:\n- ' + '\n- '.join(errors) +
                         '\nUse original Windows game Managed references with --game-managed, or a documented '
                         'public reference feed with --reference-source reference-feed and --reference-provenance. '
                         'Keep original game DLLs unchanged. See build-tools/README.md for the reference layout.')
    subprocess.run([a.pwsh, '-NoProfile', '-Command',
                    "if ($PSVersionTable.PSVersion.Major -lt 7) { exit 1 }; "
                    "Add-Type -Path (Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'); "
                    "Write-Host ('PowerShell ' + $PSVersionTable.PSVersion)"], check=True)


def compile_command(a, name, sources, ref_files, extra_refs=(), defines=(), resources=None):
    command = [sys.executable, str(HERE / 'ubuild.py'), '--pwsh', a.pwsh,
               '--out', str(a.build_dir / 'out' / name), '--unsafe']
    for source in sources:
        command += ['--src', str(source)]
    for ref_file in ref_files:
        command += ['--ref-file', str(HERE / ref_file)]
    for reference in extra_refs:
        command += ['--ref', str(reference)]
    for define in defines:
        command += ['--define', define]
    for path, resource_name in (resources or {}).items():
        command += ['--resource', str(ROOT / path) + ',' + resource_name]
    for key, value in variables(a).items():
        command += ['--var', key + '=' + value]
    return command


def commands(a):
    result = []
    for package in XR:
        result.append(compile_command(a, package['assembly'], [package_dir(a, package) / package['source']],
                                      ['common_refs.txt'],
                                      [a.build_dir / 'out' / ref for ref in package['references']], package['defines']))
    result.append([a.pwsh, '-NoProfile', '-File', str(HERE / 'gen.ps1'),
                   '-GameManaged', str(a.game_managed), '-MonoModDir', str(a.monomod),
                   '-OutPath', str(a.build_dir / 'mmhook' / 'MMHOOK_RoR2.dll')])
    for name, source in public_inputs(a).items():
        result.append([a.pwsh, '-NoProfile', '-File', str(HERE / 'publicize.ps1'),
                       '-inPath', str(source), '-outPath', str(a.refs / 'publicized' / name),
                       '-CecilPath', str(a.monomod / 'Mono.Cecil.dll'), '-searchDirs', str(a.game_managed)])
    result.append(compile_command(a, 'VRPatcher.dll', [ROOT / 'VREnabler'],
                                  ['common_refs.txt', 'bepinex_refs.txt'], resources=RESOURCES['VRPatcher']))
    result.append(compile_command(a, 'VRMod.dll', [ROOT / 'RoR2VRMod'],
                                  ['vrmod_refs.txt', 'bepinex_refs.txt'], resources=RESOURCES['VRMod']))
    result.append(compile_command(a, 'VRAPI.dll', [ROOT / 'VRAPI'],
                                  ['vrmod_refs.txt', 'bepinex_refs.txt'], [a.build_dir / 'out' / 'VRMod.dll']))
    return result


def snapshot_inputs(a):
    """Hash sources and tool inputs, including newly added files; no timestamps as proof."""
    files = set()
    for directory in ('RoR2VRMod', 'VREnabler', 'VRAPI', 'build-tools'):
        files.update(p for p in (ROOT / directory).rglob('*')
                     if p.is_file() and not {'bin', 'obj', '__pycache__'}.intersection(p.parts)
                     and p.suffix in ('.cs', '.csproj', '.ps1', '.py', '.json', '.txt'))
    files.update(ROOT / p for resources in RESOURCES.values() for p in resources)
    files.update(ROOT / p for p in (*PACKAGE_METADATA, 'manifestV2.json'))
    for directory in (a.game_managed, a.bepinex_core, a.monomod, a.refs / 'optional'):
        files.update(p for p in directory.glob('*') if p.is_file() and p.suffix.lower() in ('.dll', '.exe'))
    for package in XR:
        directory = package_dir(a, package)
        files.update(p for p in directory.rglob('*') if p.is_file() and '.git' not in p.relative_to(directory).parts
                     and (p.suffix in ('.cs', '.asmdef') or p.name == 'package.json'))
        files.update(directory / name for name in package['license_files'])
        files.update(directory / native['source'] for native in NATIVE if native['package'] == package['name'])
    if a.reference_provenance is not None:
        files.add(a.reference_provenance)
    executable = shutil.which(a.pwsh)
    if executable:
        files.add(Path(executable).resolve())
        files.update(Path(executable).resolve().parent.glob('Microsoft.CodeAnalysis*.dll'))
    return {str(p.resolve()): sha256(p) for p in sorted(files)}


def build(a):
    receipt_path = a.build_dir / 'build-receipt.json'
    # A failed attempt must invalidate an older successful build's packaging receipt.
    receipt_path.unlink(missing_ok=True)
    preflight(a)
    before = snapshot_inputs(a)
    for command in commands(a):
        print('Running:', subprocess.list2cmdline(command), flush=True)
        subprocess.run(command, cwd=ROOT, check=True)
    after = snapshot_inputs(a)
    if before != after:
        raise ValueError('Inputs changed during the build; rebuild before packaging.')
    outputs = {name: sha256(a.build_dir / 'out' / name) for name in OUTPUTS}
    if any((a.build_dir / 'out' / name).stat().st_size < 256 for name in OUTPUTS):
        raise ValueError('A compiler output is unexpectedly small; no build receipt written.')
    receipt = {'schema': 1, 'inputs': after, 'outputs': outputs,
               'commands': commands(a), 'hardware_tested': False,
               'reference_source': reference_evidence(a)}
    receipt_path.write_text(json.dumps(receipt, indent=2) + '\n')
    print(f'Full source compilation completed against {a.reference_source}. Game launch and VR hardware remain unverified.')


def package_files(a):
    receipt_path = a.build_dir / 'build-receipt.json'
    if not receipt_path.is_file():
        raise ValueError('No successful full-build receipt. Run build before packaging; preflight/plan are not builds.')
    receipt = json.loads(receipt_path.read_text())
    if receipt.get('schema') != 1 or receipt.get('inputs') != snapshot_inputs(a):
        raise ValueError('Build inputs changed or receipt is invalid. Rebuild before packaging.')
    if receipt.get('reference_source') != reference_evidence(a):
        raise ValueError('Reference provenance/options differ from the successful build; use the original options or rebuild.')
    if set(receipt.get('outputs', {})) != set(OUTPUTS):
        raise ValueError('Build receipt does not contain all required outputs.')
    files = {}
    for name in OUTPUTS:
        path = a.build_dir / 'out' / name
        if not path.is_file() or sha256(path) != receipt['outputs'][name]:
            raise ValueError(f'Missing or modified build output: {path}; rebuild before packaging.')
        files[('patchers/' if name == 'VRPatcher.dll' else 'plugins/') + name] = path
    manifest = json.loads((ROOT / 'manifest.json').read_text())
    for field in ('name', 'version_number', 'website_url', 'description', 'dependencies'):
        if field not in manifest:
            raise ValueError(f'Thunderstore manifest is missing {field}')
    if not re.fullmatch(r'[A-Za-z0-9_]+', manifest['name']) or not re.fullmatch(r'\d+\.\d+\.\d+', manifest['version_number']):
        raise ValueError('Invalid Thunderstore name or version_number')
    plugin_source = (ROOT / 'RoR2VRMod' / 'VRMod.cs').read_text(encoding='utf-8-sig')
    plugin_attribute = re.search(r'BepInPlugin\(\s*"com\.DrBibop\.VRMod"\s*,\s*"[^"]*"\s*,\s*"([^"]+)"', plugin_source)
    if not plugin_attribute or plugin_attribute.group(1) != manifest['version_number']:
        raise ValueError('Plugin version does not match manifest.json')
    for name in PACKAGE_METADATA:
        path = ROOT / name
        if not path.is_file() or not path.stat().st_size:
            raise ValueError(f'Missing package metadata: {path}')
        files[name] = path
    for dependency in XR:
        for name in dependency['license_files']:
            license_file = package_dir(a, dependency) / name
            if not license_file.is_file():
                raise ValueError(f'Missing redistribution license/notice: {license_file}')
            files['licenses/' + dependency['name'] + '-' + name] = license_file
    # Deliberately no directory copy or wildcard: game, publicized and HookGen DLLs cannot enter the ZIP.
    return manifest, files


def package(a, validate_only=False):
    manifest, files = package_files(a)
    if validate_only:
        print(f'Package inputs valid: {len(files)} allowlisted files. Hardware unverified.')
        return
    target = a.zip_path or (ROOT / 'dist' / f'{manifest["name"]}-{manifest["version_number"]}.zip')
    if target.exists() or target.is_symlink():
        raise ValueError(f'Refusing to overwrite existing release archive: {target}')
    target.parent.mkdir(parents=True, exist_ok=True)
    # mkstemp opens an unpredictable filename exclusively; existing .tmp files and
    # symlinks cannot be truncated. Keep it on the destination filesystem.
    descriptor, temp_name = tempfile.mkstemp(prefix='.' + target.name + '.', suffix='.tmp', dir=target.parent)
    temporary = Path(temp_name)
    try:
        with os.fdopen(descriptor, 'w+b') as handle, zipfile.ZipFile(handle, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
            for name, path in sorted(files.items()):
                info = zipfile.ZipInfo(name, (1980, 1, 1, 0, 0, 0))
                info.external_attr = 0o100644 << 16
                info.compress_type = zipfile.ZIP_DEFLATED
                archive.writestr(info, path.read_bytes())
        try:
            # Creating the final hard link is atomic and fails if anything already
            # occupies the name, including a file created after the initial check.
            # Filesystems without hard-link support fail safely, without a ZIP.
            os.link(temporary, target)
        except FileExistsError as error:
            raise ValueError(f'Refusing to overwrite existing release archive: {target}') from error
    finally:
        temporary.unlink(missing_ok=True)
    print(f'Created {target} (hardware unverified)')


def fetch_xr(a):
    for package in XR:
        directory = package_dir(a, package)
        if directory.exists():
            print(f'Exists, preserved: {directory} (preflight will verify it)')
            continue
        directory.parent.mkdir(parents=True, exist_ok=True)
        subprocess.run(['git', 'clone', '--depth', '1', '--branch', package['version'],
                        package['repository'], str(directory)], check=True)
        head = subprocess.check_output(['git', '-C', str(directory), 'rev-parse', 'HEAD'], text=True).strip()
        if head != package['commit']:
            raise ValueError(f'Downloaded tag does not match locked commit: {directory}; inspect before building.')


def parser():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('command', choices=['preflight', 'plan', 'fetch-xr', 'build', 'validate-package', 'package'])
    ap.add_argument('--game-managed', type=Path, default=Path(os.environ.get('ROR2_MANAGED', ROOT / 'refs' / 'game')))
    ap.add_argument('--refs', type=Path, default=ROOT / 'refs')
    ap.add_argument('--reference-source', choices=['local-files', 'reference-feed'], default='local-files',
                    help='Distinguish local reference files from documented public reference packages; neither verifies game runtime')
    ap.add_argument('--reference-provenance', type=Path, help='Reference-feed JSON source URLs and exact DLL SHA-256 hashes')
    ap.add_argument('--tools', type=Path, default=ROOT / 'build' / 'tools')
    ap.add_argument('--bepinex-core', type=Path)
    ap.add_argument('--monomod', type=Path)
    ap.add_argument('--packages', type=Path)
    ap.add_argument('--build-dir', type=Path, default=ROOT / 'build')
    ap.add_argument('--pwsh', default=os.environ.get('PWSH', 'pwsh'))
    ap.add_argument('--zip-path', type=Path)
    return ap


def arguments(argv=None):
    a = parser().parse_args(argv)
    a.bepinex_core = a.bepinex_core or a.tools / 'bepinex' / 'BepInEx' / 'core'
    a.monomod = a.monomod or a.tools / 'monomod'
    a.packages = a.packages or a.tools / 'packages'
    for key in ('game_managed', 'refs', 'tools', 'bepinex_core', 'monomod', 'packages', 'build_dir', 'reference_provenance'):
        if getattr(a, key) is not None:
            setattr(a, key, getattr(a, key).expanduser().resolve())
    if a.zip_path is not None:
        requested = a.zip_path.expanduser().absolute()
        # Resolve the parent but retain the leaf so a dangling destination symlink
        # is refused rather than followed to a different filename.
        a.zip_path = requested.parent.resolve() / requested.name
    # Resolve generated leaves too: a safe-looking refs/publicized, build/out or
    # build/mmhook can itself be a symlink into the installed game's Managed tree.
    generated_directories = (a.refs / 'publicized', a.build_dir / 'out', a.build_dir / 'mmhook')
    for requested in generated_directories:
        generated = requested.resolve()
        if generated == a.game_managed or generated in a.game_managed.parents or a.game_managed in generated.parents:
            raise ValueError(f'Generated path {requested} must be separate from --game-managed (resolves to {generated})')
        # Existing individual output links are just as dangerous as a directory link.
        for output in requested.glob('*'):
            resolved = output.resolve()
            if resolved == a.game_managed or a.game_managed in resolved.parents:
                raise ValueError(f'Generated output {output} must be separate from --game-managed')
    return a


def main(argv=None):
    a = arguments(argv)
    if a.command == 'plan':
        print(json.dumps({'status': 'plan_only_not_compiled', 'commands': commands(a)}, indent=2))
    elif a.command == 'preflight':
        preflight(a)
        print('Preflight passed. Nothing compiled; game and hardware unverified.')
    elif a.command == 'fetch-xr':
        fetch_xr(a)
    elif a.command == 'build':
        build(a)
    else:
        package(a, validate_only=a.command == 'validate-package')


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, subprocess.CalledProcessError) as error:
        print(f'error: {error}', file=sys.stderr)
        sys.exit(1)
