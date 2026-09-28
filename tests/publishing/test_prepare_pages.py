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


if __name__ == "__main__":
    unittest.main()
