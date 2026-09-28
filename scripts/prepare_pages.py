"""Prepare the published standalone Blazor app for GitHub Pages."""
import argparse
from html import escape
from html.parser import HTMLParser
from pathlib import Path


class Assets(HTMLParser):
    def __init__(self):
        super().__init__()
        self.paths = []

    def handle_starttag(self, tag, attrs):
        attrs = dict(attrs)
        if tag == "script" and attrs.get("src"):
            self.paths.append(attrs["src"])
        if tag == "link" and attrs.get("rel") == "stylesheet":
            self.paths.append(attrs["href"])


def prepare(root: Path, base_path: str):
    base = base_path.rstrip("/") + "/"
    if not base.startswith("/") or base.startswith("//") or any(c in base for c in '?#\\'):
        raise ValueError("Pages base path must be a site-relative path, such as /TimeOps/.")

    index = root / "index.html"
    html = index.read_text(encoding="utf-8")
    marker = '<base href="/" />'
    if html.count(marker) != 1:
        raise ValueError("Expected exactly one original base href in published index.html.")

    assets = Assets()
    assets.feed(html)
    for relative in [*assets.paths, "appsettings.json", "_framework/blazor.webassembly.js"]:
        file = (root / relative).resolve()
        if not file.is_relative_to(root.resolve()) or not file.is_file():
            raise ValueError(f"Missing or invalid published asset: {relative}")
    if not any((root / "_framework").glob("*.wasm")):
        raise ValueError("Published WebAssembly files are missing.")

    html = html.replace(marker, f'<base href="{escape(base, quote=True)}" />')
    index.write_text(html, encoding="utf-8")
    (root / "404.html").write_text(html, encoding="utf-8")
    (root / ".nojekyll").touch()
    # Stale compressed copies would serve the wrong base path.
    for suffix in (".gz", ".br"):
        index.with_name(index.name + suffix).unlink(missing_ok=True)
    print(f"Validated Pages artifact: {root} (base: {base})")


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("root", type=Path)
    parser.add_argument("--base-path", default="")
    args = parser.parse_args()
    prepare(args.root, args.base_path)
