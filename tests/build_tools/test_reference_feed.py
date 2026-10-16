"""Archive/cache safety tests; fixtures are never passed off as real references."""
import hashlib
import importlib.util
import io
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch
import zipfile

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('reference_feed', ROOT / 'build-tools/prepare-reference-feed.py')
feed = importlib.util.module_from_spec(spec)
spec.loader.exec_module(feed)


class ReferenceFeedSafetyTests(unittest.TestCase):
    def test_existing_output_is_untouched_before_any_download(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / 'existing'
            output.mkdir()
            sentinel = output / 'original.dll'
            sentinel.write_bytes(b'preserve original')
            with patch.object(feed, 'download') as download:
                with self.assertRaises(FileExistsError):
                    feed.prepare(output, Path(temp) / 'cache', 'pwsh', 'curl')
                download.assert_not_called()
            self.assertEqual(sentinel.read_bytes(), b'preserve original')

    def test_bad_cached_hash_is_rejected_without_download_or_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            cache = Path(temp)
            target = cache / 'sample.zip'
            target.write_bytes(b'changed archive')
            entry = {'filename': target.name, 'sha256': hashlib.sha256(b'expected').hexdigest()}
            with patch.object(feed.subprocess, 'run') as run:
                with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'):
                    feed.download(entry, cache, 'curl')
                run.assert_not_called()
            self.assertEqual(target.read_bytes(), b'changed archive')

    def test_downloaded_hash_failure_never_publishes_cache(self):
        with tempfile.TemporaryDirectory() as temp:
            cache = Path(temp)
            entry = {'filename': 'sample.zip', 'sha256': hashlib.sha256(b'expected').hexdigest(), 'url': 'https://invalid.test/test'}
            def wrong_download(command, **kwargs):
                Path(command[command.index('--output') + 1]).write_bytes(b'wrong bytes')
            with patch.object(feed.subprocess, 'run', side_effect=wrong_download):
                with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'):
                    feed.download(entry, cache, 'curl')
            self.assertEqual(list(cache.iterdir()), [])

    def test_cache_race_does_not_overwrite_another_file(self):
        with tempfile.TemporaryDirectory() as temp:
            cache = Path(temp)
            target = cache / 'sample.zip'
            entry = {'filename': target.name, 'sha256': hashlib.sha256(b'expected').hexdigest(), 'url': 'https://invalid.test/test'}
            def racing_download(command, **kwargs):
                Path(command[command.index('--output') + 1]).write_bytes(b'expected')
                target.write_bytes(b'concurrent writer')
            with patch.object(feed.subprocess, 'run', side_effect=racing_download):
                with self.assertRaisesRegex(ValueError, 'SHA-256 mismatch'):
                    feed.download(entry, cache, 'curl')
            self.assertEqual(target.read_bytes(), b'concurrent writer')
            self.assertEqual(list(cache.iterdir()), [target])

    def test_library_selection_excludes_wrong_framework_and_refuses_overwrite(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / 'test.nupkg'
            with zipfile.ZipFile(archive, 'w') as package:
                package.writestr('lib/netstandard2.0/Real.dll', b'fixture')
                package.writestr('lib/netstandard2.0/Real.xml', b'documentation')
                package.writestr('lib/net45/Other.dll', b'other framework')
            feed.extract_libraries(archive, 'lib/netstandard2.0/', root / 'refs')
            self.assertEqual([p.name for p in (root / 'refs').iterdir()], ['Real.dll'])
            (root / 'refs/Real.dll').write_bytes(b'preserve existing')
            with self.assertRaises(FileExistsError):
                feed.extract_libraries(archive, 'lib/netstandard2.0/', root / 'refs')
            self.assertEqual((root / 'refs/Real.dll').read_bytes(), b'preserve existing')

    def test_archive_traversal_and_symlink_are_rejected(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            archive = root / 'test.nupkg'
            with zipfile.ZipFile(archive, 'w') as package:
                package.writestr('lib/netstandard2.0/../../escape.dll', b'fixture')
            with self.assertRaisesRegex(ValueError, 'Unsafe archive'):
                feed.extract_libraries(archive, 'lib/netstandard2.0/', root / 'refs')
            for member_name, member_type in (('package/Runtime/../../escape.cs', tarfile.REGTYPE),
                                            ('package/Runtime/symlink.cs', tarfile.SYMTYPE)):
                with self.subTest(member=member_name):
                    archive = root / 'test.tgz'
                    with tarfile.open(archive, 'w:gz') as package:
                        member = tarfile.TarInfo(member_name)
                        member.type = member_type
                        member.size = 7 if member_type == tarfile.REGTYPE else 0
                        member.linkname = '/tmp/escape.cs' if member_type == tarfile.SYMTYPE else ''
                        package.addfile(member, io.BytesIO(b'fixture'))
                    with self.assertRaises(ValueError):
                        feed.extract_legacy_sources(archive, root / 'sources')
            self.assertFalse((root / 'escape.dll').exists())
            self.assertFalse((root / 'escape.cs').exists())

    def test_cache_and_output_paths_cannot_alias(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            with self.assertRaisesRegex(ValueError, 'separate directories'):
                feed.main(['--output', str(root / 'output'), '--cache', str(root / 'output/cache')])
            target = root / 'target'
            target.mkdir()
            link = root / 'cache-link'
            try:
                link.symlink_to(target, target_is_directory=True)
            except OSError as error:
                self.skipTest(f'Symlinks unavailable: {error}')
            with self.assertRaisesRegex(ValueError, 'must not be a symlink'):
                feed.main(['--output', str(root / 'output'), '--cache', str(link)])


if __name__ == '__main__':
    unittest.main()
