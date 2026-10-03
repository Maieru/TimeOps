import importlib.util
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location(
    "prepare_pages", Path(__file__).resolve().parents[2] / "scripts" / "prepare_pages.py")
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class PagesPreparationTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "_framework").mkdir()
        for name in ["app.css", "appsettings.json", "_framework/blazor.webassembly.js",
                     "_framework/app.wasm", "index.html.br", "index.html.gz"]:
            (self.root / name).touch()
        (self.root / "index.html").write_text(
            '<base href="/" /><link rel="stylesheet" href="app.css" />'
            '<script src="_framework/blazor.webassembly.js"></script>', encoding="utf-8")

    def test_project_subdirectory_and_fallback(self):
        module.prepare(self.root, "/TimeOps")
        html = (self.root / "index.html").read_text(encoding="utf-8")
        self.assertIn('<base href="/TimeOps/" />', html)
        self.assertEqual(html, (self.root / "404.html").read_text(encoding="utf-8"))
        self.assertTrue((self.root / ".nojekyll").is_file())
        self.assertFalse((self.root / "index.html.br").exists())
        self.assertFalse((self.root / "index.html.gz").exists())

    def test_root_domain(self):
        module.prepare(self.root, "")
        self.assertIn('<base href="/" />', (self.root / "index.html").read_text())

    def test_missing_runtime_fails_before_modifying_index(self):
        (self.root / "_framework/app.wasm").unlink()
        with self.assertRaisesRegex(ValueError, "WebAssembly"):
            module.prepare(self.root, "/TimeOps")
        self.assertFalse((self.root / "404.html").exists())

    def test_missing_stylesheet_fails(self):
        (self.root / "app.css").unlink()
        with self.assertRaisesRegex(ValueError, "app.css"):
            module.prepare(self.root, "/TimeOps")

    def test_changed_html_fails_instead_of_publishing_wrong_base(self):
        (self.root / "index.html").write_text('<base href="/unexpected/" />')
        with self.assertRaisesRegex(ValueError, "base href"):
            module.prepare(self.root, "/TimeOps")

    def test_external_base_is_rejected(self):
        with self.assertRaises(ValueError):
            module.prepare(self.root, "//example.com")

    def test_invalid_base_paths_leave_artifact_unchanged(self):
        original = (self.root / "index.html").read_bytes()
        for base in ["TimeOps", "https://example.com/", "/TimeOps?query=1",
                     "/TimeOps#fragment", "/TimeOps\\nested"]:
            with self.subTest(base=base):
                with self.assertRaisesRegex(ValueError, "site-relative"):
                    module.prepare(self.root, base)
                self.assertEqual(original, (self.root / "index.html").read_bytes())
                self.assertFalse((self.root / "404.html").exists())
                self.assertFalse((self.root / ".nojekyll").exists())
                self.assertTrue((self.root / "index.html.gz").exists())
                self.assertTrue((self.root / "index.html.br").exists())

    def test_multiple_base_markers_are_rejected(self):
        original = '<base href="/" /><base href="/" />'
        (self.root / "index.html").write_text(original, encoding="utf-8")
        with self.assertRaisesRegex(ValueError, "exactly one"):
            module.prepare(self.root, "/TimeOps")
        self.assertEqual(original, (self.root / "index.html").read_text(encoding="utf-8"))
        self.assertFalse((self.root / "404.html").exists())

    def test_asset_outside_publish_directory_is_rejected_even_if_it_exists(self):
        with tempfile.TemporaryDirectory() as outside:
            asset = Path(outside) / "external.css"
            asset.touch()
            original = f'<base href="/" /><link rel="stylesheet" href="{asset.as_posix()}" />'
            (self.root / "index.html").write_text(original, encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "invalid published asset"):
                module.prepare(self.root, "/TimeOps")
            self.assertEqual(original, (self.root / "index.html").read_text(encoding="utf-8"))
            self.assertFalse((self.root / "404.html").exists())

    def test_trailing_slash_and_missing_compressed_copies_are_supported(self):
        (self.root / "index.html.br").unlink()
        (self.root / "index.html.gz").unlink()
        module.prepare(self.root, "/TimeOps/")
        html = (self.root / "index.html").read_text(encoding="utf-8")
        self.assertIn('<base href="/TimeOps/" />', html)
        self.assertEqual(html, (self.root / "404.html").read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
