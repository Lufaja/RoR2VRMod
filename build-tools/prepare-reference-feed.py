#!/usr/bin/env python3
"""Recreate the pinned public compile references. Never install or package them."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import shutil
import subprocess
import sys
import tarfile
import tempfile
import zipfile

HERE = Path(__file__).resolve().parent
ROOT = HERE.parent


def sha256(path):
    with path.open('rb') as stream:
        digest = hashlib.sha256()
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def require_hash(path, expected):
    if path.is_symlink() or not path.is_file() or sha256(path) != expected:
        raise ValueError(f'Archive/file SHA-256 mismatch or unsafe file: {path}; preserve it and use a fresh cache.')


def safe_relative(value):
    path = PurePosixPath(value)
    if not value or '\\' in value or ':' in value or path.is_absolute() or '..' in path.parts:
        raise ValueError(f'Unsafe archive/lock path: {value}')
    return path


def download(entry, cache, curl):
    name = safe_relative(entry['filename'])
    if len(name.parts) != 1:
        raise ValueError('Cache filename must be a single path component')
    target = cache / str(name)
    expected = entry.get('archive_sha256', entry.get('sha256'))
    if target.exists() or target.is_symlink():
        require_hash(target, expected)
        return target
    cache.mkdir(parents=True, exist_ok=True)
    descriptor, filename = tempfile.mkstemp(prefix=name.name + '.', suffix='.tmp', dir=cache)
    os.close(descriptor)
    temporary = Path(filename)
    try:
        print(f'Downloading pinned {name.name}', flush=True)
        subprocess.run([curl, '--fail', '--location', '--silent', '--show-error',
                        '--retry', '3', '--connect-timeout', '30', '--max-time', '300',
                        '--proto', '=https', '--proto-redir', '=https',
                        '--output', str(temporary), entry['url']], check=True)
        require_hash(temporary, expected)
        try:
            os.link(temporary, target)  # Exclusive publication; never replace cached files.
        except FileExistsError:
            require_hash(target, expected)
    finally:
        temporary.unlink(missing_ok=True)
    return target


def write_new(path, contents):
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open('xb') as stream:
        stream.write(contents)


def extract_libraries(archive, prefix, destination):
    safe_relative(prefix)
    count = 0
    with zipfile.ZipFile(archive) as package:
        for member in package.infolist():
            if not member.filename.startswith(prefix) or member.is_dir():
                continue
            relative = safe_relative(member.filename[len(prefix):])
            if relative.suffix.lower() not in ('.dll', '.exe'):
                continue
            if len(relative.parts) != 1:
                raise ValueError(f'Unexpected nested library: {member.filename}')
            write_new(destination / relative.name, package.read(member))
            count += 1
    if not count:
        raise ValueError(f'No libraries under {prefix} in {archive}')


def extract_legacy_sources(archive, destination):
    count = 0
    with tarfile.open(archive) as package:
        for member in package.getmembers():
            if not member.name.startswith('package/Runtime/'):
                continue
            relative = safe_relative(member.name)
            if member.isdir():
                continue
            if not member.isfile():
                raise ValueError(f'Non-regular source archive entry: {member.name}')
            with package.extractfile(member) as stream:
                write_new(destination / str(relative), stream.read())
            count += 1
    if not count:
        raise ValueError(f'No runtime sources in {archive}')


def run_ps(pwsh, script, *args):
    subprocess.run([pwsh, '-NoProfile', '-NonInteractive', '-File', str(HERE / script),
                    *map(str, args)], check=True, cwd=ROOT)


def prepare(output, cache, pwsh, curl):
    # mkdir is exclusive even for dangling symlinks. No operation touches an existing output.
    output.mkdir(parents=True, exist_ok=False)
    lock = json.loads((HERE / 'reference-feed.lock.json').read_text())
    refs = output / 'refs'
    for entry in lock['packages']:
        archive = download(entry, cache, curl)
        if entry['extract_prefix']:
            extract_libraries(archive, entry['extract_prefix'], output / str(safe_relative(entry['destination'])))
    for name in ('Mono.Cecil.dll', 'Mono.Cecil.Rocks.dll', 'MonoMod.Utils.dll', 'MonoMod.RuntimeDetour.dll'):
        write_new(output / 'bepinex/BepInEx/core' / name, (output / 'monomod' / name).read_bytes())

    original = output / 'originals/UnityEngine.UI.dll'
    require_hash(refs / original.name, lock['ui_visibility']['original_sha256'])
    original.parent.mkdir()
    # Move only the newly extracted reference. Keep the original as immutable evidence.
    (refs / original.name).rename(original)
    run_ps(pwsh, 'restore-ui-reference-visibility.ps1',
           '-source', original, '-destination', refs / original.name,
           '-CecilPath', output / 'monomod/Mono.Cecil.dll', '-SearchDir', refs,
           '-ProvenancePath', output / 'ui-visibility-provenance.json',
           '-ExpectedSourceSha256', lock['ui_visibility']['original_sha256'])

    legacy = lock['legacy_input_helpers']
    sources = output / 'sources/legacyinputhelpers'
    extract_legacy_sources(download(legacy, cache, curl), sources)
    runtime = sources / 'package/Runtime'
    for name, source in (('UnityEngine.SpatialTracking.dll', runtime / 'TrackedPoseDriver'),
                         ('UnityEngine.XR.LegacyInputHelpers.dll', runtime)):
        command = [sys.executable, str(HERE / 'ubuild.py'), '--pwsh', pwsh,
                   '--out', str(refs / name), '--src', str(source), '--unsafe']
        for reference in sorted(refs.glob('*.dll')):
            command += ['--ref', str(reference)]
        subprocess.run(command, check=True, cwd=ROOT)

    haptics = lock['haptics']
    with zipfile.ZipFile(download(haptics, cache, curl)) as package:
        patcher = output / 'sources/upstream-VRPatcher.dll'
        write_new(patcher, package.read(haptics['patcher_entry']))
    require_hash(patcher, haptics['patcher_sha256'])
    run_ps(pwsh, 'extract-haptics-references.ps1', '-AssemblyPath', patcher, '-Destination', output / 'optional')
    for name, expected in haptics['files'].items():
        require_hash(output / 'optional' / name, expected)

    evidence = {
        'schema': 1, 'kind': 'public-reference-feed',
        'purpose': lock['purpose'], 'game_runtime_verified': False, 'hardware_tested': False,
        'sources': lock['packages'] + [legacy, haptics],
        'ui_visibility': json.loads((output / 'ui-visibility-provenance.json').read_text(encoding='utf-8-sig')),
        'reference_sources_lock_sha256': sha256(HERE / 'reference-feed.lock.json'),
        'files': {p.name: sha256(p) for p in sorted(refs.glob('*.dll'))},
    }
    write_new(refs / 'provenance.json', (json.dumps(evidence, indent=2) + '\n').encode())
    print(f'Prepared {len(evidence["files"])} real public reference DLLs in {refs}.')
    print('Compile references only; no game launch or hardware verification. Never package this directory.')


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'build/reference-feed', help='Must not exist, including an empty directory')
    parser.add_argument('--cache', type=Path, default=ROOT / 'build/tools/reference-cache')
    parser.add_argument('--pwsh', default=os.environ.get('PWSH', 'pwsh'))
    parser.add_argument('--curl', default='curl')
    args = parser.parse_args(argv)
    # Resolve parents without following an existing output/cache leaf symlink.
    output = args.output.absolute().parent.resolve() / args.output.name
    cache = args.cache.absolute().parent.resolve() / args.cache.name
    if output == cache or output in cache.parents or cache in output.parents:
        raise ValueError('Output and download cache must be separate directories')
    if cache.is_symlink():
        raise ValueError('Download cache must not be a symlink')
    for program in (args.pwsh, args.curl):
        if not shutil.which(program):
            raise ValueError(f'Required tool not found: {program}')
    prepare(output, cache, args.pwsh, args.curl)
    return 0


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, OSError, subprocess.CalledProcessError, zipfile.BadZipFile, tarfile.TarError) as error:
        sys.exit(f'Reference preparation failed: {error}\nNo new success provenance was written. Preserve partial output and retry with a new --output directory.')
