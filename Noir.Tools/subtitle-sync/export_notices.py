import importlib.metadata
import sys
from pathlib import Path

target = Path(sys.argv[1]) / "licenses"
target.mkdir(exist_ok=True)
for dist in importlib.metadata.distributions():
    name = dist.metadata.get("Name", "dependency")
    folder = target / name
    folder.mkdir(exist_ok=True)
    (folder / "NOTICE.txt").write_text(dist.read_text("METADATA") or dist.read_text("PKG-INFO") or name, encoding="utf-8")
    for file in dist.files or []:
        if file.name.lower().startswith(("license", "copying", "copyright")):
            source = dist.locate_file(file)
            if source.is_file():
                (folder / file.name).write_bytes(source.read_bytes())
