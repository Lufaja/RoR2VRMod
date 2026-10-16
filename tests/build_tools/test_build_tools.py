"""Isolated build-safety tests. Synthetic fixtures never become release artifacts."""
import importlib.util
import json
import os
import shutil
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]


def load_module(name):
    spec = importlib.util.spec_from_file_location(name, ROOT / 'build-tools' / (name + '.py'))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


build = load_module('build')
ubuild = load_module('ubuild')


class SourceGatherTests(unittest.TestCase):
    def test_skips_editor_tests_outputs_and_nested_assemblies(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp).resolve()
            for name in ('Runtime/ok.cs', 'Editor/no.cs', 'Tests/no.cs', 'obj/no.cs', 'bin/no.cs',
                         'Nested/no.cs', 'Nested/Other.asmdef', 'Samples~/no.cs'):
                path = root / name
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text('')
            self.assertEqual(ubuild.gather(str(root), set()), [str(root / 'Runtime/ok.cs')])

    def test_response_file_preserves_spaces_and_preferred_public_refs(self):
        with tempfile.TemporaryDirectory(prefix='vr build ') as temp:
            root = Path(temp).resolve()
            for directory in ('source', 'public', 'game'):
                (root / directory).mkdir()
            (root / 'source/Example.cs').write_text('class Example {}')
            (root / 'public/Game.dll').write_bytes(b'public test fixture')
            (root / 'game/Game.dll').write_bytes(b'private test fixture')
            (root / 'asset file').write_bytes(b'test resource')
            (root / 'refs.txt').write_text('${PUBLIC}/Game.dll\n')
            output = root / 'out.dll'
            result = subprocess.run([sys.executable, str(ROOT / 'build-tools/ubuild.py'), '--plan',
                                     '--src', str(root / 'source'), '--out', str(output),
                                     '--ref-file', str(root / 'refs.txt'), '--var', f'PUBLIC={root / "public"}',
                                     '--ref', str(root / 'game/Game.dll'),
                                     '--resource', f'{root / "asset file"},Example.Asset'], capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            response = Path(str(output) + '.rsp').read_text()
            # Explicit refs have priority over ref-files, allowing deliberate overrides.
            self.assertIn(f'/r:"{root / "game/Game.dll"}"', response)
            self.assertNotIn(f'/r:"{root / "public/Game.dll"}"', response)
            self.assertIn(f'/resource:"{root / "asset file"}",Example.Asset', response)
            self.assertFalse(output.exists())

    def test_missing_source_directory_is_actionable(self):
        with self.assertRaisesRegex(ValueError, 'source directory not found'):
            ubuild.gather('/does-not-exist-riskofvr-test', set())


@unittest.skipUnless(shutil.which(os.environ.get('PWSH', 'pwsh')), 'PowerShell 7 not available; set PWSH for compiler smoke tests')
class CompilerSmokeTests(unittest.TestCase):
    def test_compile_embedded_resource_and_remove_output_on_failure(self):
        pwsh = Path(shutil.which(os.environ.get('PWSH', 'pwsh'))).resolve()
        with tempfile.TemporaryDirectory(prefix='vr compiler smoke ') as temp:
            root = Path(temp).resolve()
            source = root / 'Smoke.cs'
            source.write_text('public class Smoke { public const int Value = 42; }')
            (root / 'sample asset').write_bytes(b'expected resource bytes')
            output = root / 'Smoke.dll'
            command = [sys.executable, str(ROOT / 'build-tools/ubuild.py'), '--pwsh', str(pwsh),
                       '--out', str(output), '--src', str(root), '--no-unity-defines',
                       '--ref', str(pwsh.parent / 'System.Private.CoreLib.dll'),
                       '--resource', str(root / 'sample asset') + ',Smoke.Asset']
            result = subprocess.run(command, capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            check = root / 'check.ps1'
            check.write_text('param([string]$AssemblyPath)\n'
                             '$a = [Reflection.Assembly]::LoadFile($AssemblyPath)\n'
                             '$s = $a.GetManifestResourceStream("Smoke.Asset")\n'
                             '$r = [IO.StreamReader]::new($s)\n'
                             'if ($r.ReadToEnd() -ne "expected resource bytes") { throw "resource mismatch" }\n')
            verify = subprocess.run([str(pwsh), '-NoProfile', '-File', str(check), str(output)], capture_output=True, text=True)
            self.assertEqual(verify.returncode, 0, verify.stderr)
            source.write_text('this is not valid C#')
            failure = subprocess.run(command, capture_output=True, text=True)
            self.assertNotEqual(failure.returncode, 0)
            self.assertFalse(output.exists(), 'failed compilation must not leave the previous successful DLL')


class BuildSafetyTests(unittest.TestCase):
    def test_plan_keeps_all_resource_names_and_order(self):
        a = build.arguments(['plan'])
        plan = build.commands(a)
        compilation = [command for command in plan if '--out' in command]
        self.assertEqual([Path(command[command.index('--out') + 1]).name for command in compilation], build.OUTPUTS)
        all_args = '\n'.join(arg for command in plan for arg in command)
        for resources in build.RESOURCES.values():
            for name in resources.values():
                self.assertIn(name, all_args)
        # Python/PowerShell may legitimately live under /opt (GitHub runners do).
        # Check resolved build/tool paths rather than banning host directories.
        for command in compilation:
            self.assertEqual(command[0], sys.executable)
            self.assertEqual(command[1], str(build.HERE / 'ubuild.py'))
            self.assertEqual(Path(command[command.index('--out') + 1]).parent, a.build_dir / 'out')
            self.assertEqual(command[command.index('--pwsh') + 1], a.pwsh)
        for command in plan:
            if '-File' in command:
                self.assertEqual(Path(command[command.index('-File') + 1]).parent, build.HERE)

    def test_original_game_directory_cannot_be_publicized(self):
        with self.assertRaisesRegex(ValueError, 'must be separate'):
            build.arguments(['plan', '--game-managed', '/tmp/refs/publicized', '--refs', '/tmp/refs'])


    def test_generated_directory_symlinks_cannot_target_game(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            game = root / 'game'
            game.mkdir()
            sentinel = game / 'original.dll'
            sentinel.write_bytes(b'keep original')
            for relative in ('refs/publicized', 'build/out', 'build/mmhook'):
                with self.subTest(relative=relative):
                    linked = root / relative
                    linked.parent.mkdir(parents=True, exist_ok=True)
                    try:
                        linked.symlink_to(game, target_is_directory=True)
                    except OSError as error:
                        self.skipTest(f'Symlinks unavailable: {error}')
                    try:
                        with self.assertRaisesRegex(ValueError, 'must be separate'):
                            build.arguments(['plan', '--game-managed', str(game), '--refs', str(root / 'refs'), '--build-dir', str(root / 'build')])
                        self.assertEqual(sentinel.read_bytes(), b'keep original')
                    finally:
                        linked.unlink()

    def test_generated_file_symlink_cannot_target_game(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            game = root / 'game'
            game.mkdir()
            original = game / 'RoR2.dll'
            original.write_bytes(b'keep original')
            output = root / 'refs/publicized/RoR2.dll'
            output.parent.mkdir(parents=True)
            try:
                output.symlink_to(original)
            except OSError as error:
                self.skipTest(f'Symlinks unavailable: {error}')
            with self.assertRaisesRegex(ValueError, 'must be separate'):
                build.arguments(['plan', '--game-managed', str(game), '--refs', str(root / 'refs')])
            self.assertEqual(original.read_bytes(), b'keep original')

    def test_missing_references_are_reported_together(self):
        with tempfile.TemporaryDirectory() as temp:
            a = build.arguments(['preflight', '--game-managed', temp + '/game', '--refs', temp + '/refs',
                                 '--tools', temp + '/tools', '--pwsh', temp + '/missing-pwsh'])
            errors = build.dependency_errors(a)
            self.assertTrue(any('RoR2.dll' in error for error in errors))
            self.assertTrue(any('PowerShell 7 not found' in error for error in errors))
            self.assertTrue(any('fetch-xr' in error for error in errors))
            self.assertFalse((Path(temp) / 'game').exists())

    def test_native_dll_mismatch_is_reported_before_compile(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            a = build.arguments(['preflight', '--packages', str(root / 'packages')])
            package = next(item for item in build.XR if item['name'] == 'com.unity.xr.openxr')
            native = dict(build.NATIVE[0])
            resource = root / native['resource']
            source = build.package_dir(a, package) / native['source']
            for path in (resource, source):
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_bytes(b'same locked fixture')
            native['sha256'] = build.sha256(resource)
            with patch.object(build, 'ROOT', root), patch.object(build, 'NATIVE', [native]):
                self.assertEqual(build.native_dependency_errors(a), [])
                resource.write_bytes(b'managed/native version mismatch')
                self.assertTrue(any(str(resource) in error and 'differs from locked' in error for error in build.native_dependency_errors(a)))
                resource.write_bytes(b'same locked fixture')
                source.write_bytes(b'changed source native')
                self.assertTrue(any(str(source) in error and 'differs from locked' in error for error in build.native_dependency_errors(a)))
                source.unlink()
                self.assertTrue(any('Missing locked native DLL' in error for error in build.native_dependency_errors(a)))

    def test_reference_feed_requires_exact_provenance(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            a = build.arguments(['plan', '--game-managed', temp, '--reference-source', 'reference-feed',
                                 '--reference-provenance', str(root / 'provenance.json')])
            with self.assertRaisesRegex(ValueError, 'require --reference-provenance'):
                build.reference_evidence(a)
            reference = root / 'RoR2.dll'
            reference.write_bytes(b'disposable metadata fixture')
            manifest = {'schema': 1, 'kind': 'public-reference-feed', 'sources': [{'url': 'https://example.com/fixture'}],
                        'files': {'RoR2.dll': build.sha256(reference)}}
            a.reference_provenance.write_text(json.dumps(manifest))
            evidence = build.reference_evidence(a)
            self.assertEqual(evidence['kind'], 'reference-feed')
            self.assertFalse(evidence['game_runtime_verified'])
            reference.write_bytes(b'altered reference')
            with self.assertRaisesRegex(ValueError, 'differ from their provenance'):
                build.reference_evidence(a)

    def test_failed_preflight_invalidates_old_receipt(self):
        with tempfile.TemporaryDirectory() as temp:
            a = build.arguments(['build', '--build-dir', temp])
            receipt = Path(temp) / 'build-receipt.json'
            receipt.write_text('{}')
            with patch.object(build, 'preflight', side_effect=ValueError('missing game')):
                with self.assertRaisesRegex(ValueError, 'missing game'):
                    build.build(a)
            self.assertFalse(receipt.exists())


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.args = build.arguments(['package', '--build-dir', str(self.root / 'build'),
                                     '--tools', str(self.root / 'tools'), '--refs', str(self.root / 'refs'),
                                     '--game-managed', str(self.root / 'game'), '--pwsh', 'nonexistent-test-pwsh'])
        self.root_patch = patch.object(build, 'ROOT', self.root)
        self.root_patch.start()
        self.addCleanup(self.root_patch.stop)
        for name in (*build.PACKAGE_METADATA, 'manifestV2.json'):
            path = self.root / name
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text('fixture')
        (self.root / 'manifest.json').write_text(json.dumps({'name': 'TestMod', 'version_number': '1.0.0',
                                                            'website_url': 'https://example.com', 'description': 'test', 'dependencies': []}))
        source = self.root / 'RoR2VRMod/VRMod.cs'
        source.parent.mkdir()
        source.write_text('[BepInPlugin("com.DrBibop.VRMod", "Test", "1.0.0")]')
        for resources in build.RESOURCES.values():
            for relative in resources:
                target = self.root / relative
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b'resource fixture')
        for dependency in build.XR:
            directory = build.package_dir(self.args, dependency)
            directory.mkdir(parents=True)
            for name in dependency['license_files']:
                (directory / name).write_text('license/notice fixture')
            for native in build.NATIVE:
                if native['package'] == dependency['name']:
                    native_source = directory / native['source']
                    native_source.parent.mkdir(parents=True, exist_ok=True)
                    native_source.write_bytes(b'native source fixture')
        out = self.args.build_dir / 'out'
        out.mkdir(parents=True)
        for name in build.OUTPUTS:
            (out / name).write_bytes(b'isolated fixture, not a compiled DLL')
        (out / 'RoR2.dll').write_bytes(b'NEVER PACKAGE THIS')
        self.receipt = self.args.build_dir / 'build-receipt.json'
        self.write_receipt()

    def write_receipt(self):
        self.receipt.write_text(json.dumps({'schema': 1, 'inputs': build.snapshot_inputs(self.args),
                                            'reference_source': build.reference_evidence(self.args),
                                            'outputs': {name: build.sha256(self.args.build_dir / 'out' / name) for name in build.OUTPUTS}}))


    def test_changed_restore_script_and_docs_require_rebuild(self):
        for relative in ('tools/restore-native.py', 'docs/STEAM-MACHINE.md', 'docs/VERIFICATION.md'):
            with self.subTest(relative=relative):
                path = self.root / relative
                path.write_text(path.read_text() + 'changed')
                with self.assertRaisesRegex(ValueError, 'inputs changed'):
                    build.package_files(self.args)
                self.write_receipt()

    def test_predictable_temp_symlink_is_never_touched(self):
        self.args.zip_path = self.root / 'fixture.zip'
        original = self.root / 'valuable-file'
        original.write_bytes(b'keep original')
        old_temp = self.root / 'fixture.zip.tmp'
        try:
            old_temp.symlink_to(original)
        except OSError as error:
            self.skipTest(f'Symlinks unavailable: {error}')
        build.package(self.args)
        self.assertEqual(original.read_bytes(), b'keep original')
        self.assertTrue(old_temp.is_symlink())
        self.assertFalse(list(self.root.glob('.fixture.zip.*.tmp')))

    def test_racing_final_destination_is_not_overwritten(self):
        self.args.zip_path = self.root / 'fixture.zip'
        real_link = os.link
        def concurrent_publish(source, destination):
            Path(destination).write_bytes(b'other build already published')
            return real_link(source, destination)
        with patch.object(build.os, 'link', side_effect=concurrent_publish):
            with self.assertRaisesRegex(ValueError, 'Refusing to overwrite'):
                build.package(self.args)
        self.assertEqual(self.args.zip_path.read_bytes(), b'other build already published')
        self.assertFalse(list(self.root.glob('.fixture.zip.*.tmp')))

    def test_dangling_destination_symlink_is_not_followed(self):
        target = self.root / 'fixture.zip'
        elsewhere = self.root / 'elsewhere.zip'
        try:
            target.symlink_to(elsewhere)
        except OSError as error:
            self.skipTest(f'Symlinks unavailable: {error}')
        cli = build.arguments(['package', '--zip-path', str(target)])
        self.assertEqual(cli.zip_path, target.parent.resolve() / target.name)
        self.args.zip_path = target
        with self.assertRaisesRegex(ValueError, 'Refusing to overwrite'):
            build.package(self.args)
        self.assertTrue(target.is_symlink())
        self.assertFalse(elsewhere.exists())

    def test_third_party_notice_and_native_source_changes_require_rebuild(self):
        package = next(item for item in build.XR if item['name'] == 'com.unity.xr.openxr')
        directory = build.package_dir(self.args, package)
        for relative in ('Third Party Notices.md', build.NATIVE[0]['source']):
            with self.subTest(relative=relative):
                path = directory / relative
                path.write_bytes(path.read_bytes() + b'changed')
                with self.assertRaisesRegex(ValueError, 'inputs changed'):
                    build.package_files(self.args)
                self.write_receipt()

    def test_missing_receipt_refuses_packaging(self):
        self.receipt.unlink()
        with self.assertRaisesRegex(ValueError, 'No successful full-build receipt'):
            build.package_files(self.args)

    def test_changed_source_and_missing_output_are_rejected(self):
        (self.root / 'RoR2VRMod/NewSource.cs').write_text('class NewlyAdded {}')
        with self.assertRaisesRegex(ValueError, 'inputs changed'):
            build.package_files(self.args)
        self.write_receipt()
        (self.args.build_dir / 'out/VRMod.dll').unlink()
        with self.assertRaisesRegex(ValueError, 'Missing or modified build output'):
            build.package_files(self.args)

    def test_modified_dll_and_mismatched_version_are_rejected(self):
        (self.args.build_dir / 'out/VRMod.dll').write_bytes(b'tampered fixture')
        with self.assertRaisesRegex(ValueError, 'Missing or modified build output'):
            build.package_files(self.args)
        manifest = self.root / 'manifest.json'
        manifest.write_text(manifest.read_text().replace('1.0.0', '2.0.0'))
        self.write_receipt()
        with self.assertRaisesRegex(ValueError, 'Plugin version does not match'):
            build.package_files(self.args)

    def test_zip_is_allowlisted_and_cannot_overwrite_release(self):
        # Archive is only in this test's disposable directory and is destroyed in tearDown.
        self.args.zip_path = self.root / 'fixture.zip'
        build.package(self.args)
        with zipfile.ZipFile(self.args.zip_path) as archive:
            self.assertNotIn('plugins/RoR2.dll', archive.namelist())
            self.assertIn('patchers/VRPatcher.dll', archive.namelist())
            self.assertIn('THIRD-PARTY-NOTICES.md', archive.namelist())
            self.assertTrue(set(build.PACKAGE_METADATA).issubset(archive.namelist()))
            self.assertEqual(len([name for name in archive.namelist() if name.startswith('licenses/')]), sum(len(item['license_files']) for item in build.XR))
            self.assertIn('licenses/com.unity.xr.openxr-Third Party Notices.md', archive.namelist())
            self.assertTrue(all('publicized' not in name and 'MMHOOK' not in name for name in archive.namelist()))
        with self.assertRaisesRegex(ValueError, 'Refusing to overwrite'):
            build.package(self.args)


if __name__ == '__main__':
    unittest.main()
