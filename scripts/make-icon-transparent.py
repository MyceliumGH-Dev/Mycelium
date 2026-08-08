from pathlib import Path
from PIL import Image

path = Path(__file__).resolve().parents[1] / "src" / "Mycelium" / "Icons" / "MyceliumGreenNetwork.png"
image = Image.open(path).convert("RGBA")
pixels = []
for red, green, blue, alpha in image.getdata():
    distance = max(red, green, blue) - min(red, green, blue)
    if min(red, green, blue) > 248 and distance < 4:
        pixels.append((red, green, blue, 0))
    else:
        pixels.append((red, green, blue, alpha))
image.putdata(pixels)
image.save(path)
