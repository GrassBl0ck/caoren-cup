"""Bundle the portable program with its adjacent gallery for a first install."""
import sys
from pathlib import Path
from tempfile import NamedTemporaryFile
from zipfile import ZIP_DEFLATED, ZipFile


def build_archive(executable: Path, gallery: Path, archive: Path) -> None:
    with NamedTemporaryFile(dir=archive.parent, suffix=".zip.tmp", delete=False) as staging:
        temporary = Path(staging.name)
    try:
        with ZipFile(temporary, "w", compression=ZIP_DEFLATED, compresslevel=1) as bundle:
            bundle.write(executable, executable.name)
            bundle.write(Path(__file__).resolve().parents[1] / "SERVER-SETUP.md", "SERVER-SETUP.md")
            for name in ("index.json", "images.pack"):
                bundle.write(gallery / name, f"image-bundle/{name}")
        temporary.replace(archive)
    finally:
        temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    build_archive(*map(Path, sys.argv[1:]))
