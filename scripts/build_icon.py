"""Convert the approved PNG artwork to Windows icon sizes (requires Pillow)."""
from pathlib import Path
from PIL import Image

assets = Path(__file__).resolve().parents[1] / "src" / "HttpTester.App" / "Assets"
with Image.open(assets / "HttpDetector.source.png") as source:
    source = source.convert("RGBA")
    if source.width != source.height:
        raise ValueError("Icon artwork must be square.")
    source.resize((256, 256), Image.Resampling.LANCZOS).save(assets / "HttpDetector.png", optimize=True)
    sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]
    source.save(assets / "HttpDetector.ico", format="ICO", sizes=[(size, size) for size in sizes])

with Image.open(assets / "HttpDetector.ico") as icon:
    actual_sizes = icon.ico.sizes()
    assert actual_sizes == {(size, size) for size in sizes}, actual_sizes
    assert icon.mode == "RGBA"
    print("ICO verified:", sorted(actual_sizes))
