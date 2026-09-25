"""The app's icons and Play graphics, drawn from the website's favicon: the pin.

White pin on the one blue, with the blue showing through its eye. Drawn at 4x
and scaled down, which is the anti-aliasing.
"""
import math
import os
from PIL import Image, ImageDraw, ImageFont

BLUE = (0, 102, 204, 255)        # #0066CC, the accent everywhere else
WHITE = (255, 255, 255, 255)
CLEAR = (0, 0, 0, 0)

ART = 'C:/Users/zalex/ARTrail/Assets/Art/Icon'
STORE = 'C:/Users/zalex/Desktop/NOIT APP/docs/play-store'
FONT = 'C:/Users/zalex/ARTrail/Assets/Fonts/Inter/Inter-SemiBold.ttf'
FONT_REG = 'C:/Users/zalex/ARTrail/Assets/Fonts/Inter/Inter-Regular.ttf'
os.makedirs(ART, exist_ok=True)
os.makedirs(STORE, exist_ok=True)


def pin(draw, cx, top, height, colour, hole):
    """The favicon's pin: a circle and the two tangents to its tip."""
    r = height * 6.0 / 15.6            # favicon: radius 6 over a 15.6 high pin
    cy = top + r
    tip = top + height
    d = tip - cy
    a = math.asin(r / d)               # half-angle of the tangents at the tip
    # tangent points, measured from the centre
    t = math.pi / 2 - a
    left = (cx - r * math.cos(math.pi / 2 - t), cy + r * math.sin(math.pi / 2 - t))
    right = (cx + r * math.cos(math.pi / 2 - t), cy + r * math.sin(math.pi / 2 - t))
    draw.ellipse([cx - r, cy - r, cx + r, cy + r], fill=colour)
    draw.polygon([left, right, (cx, tip)], fill=colour)
    h = r * 2.1 / 6.0
    draw.ellipse([cx - h, cy - h, cx + h, cy + h], fill=hole)


def render(size, background, shape, pin_height_share, name, folder=ART):
    k = 4
    s = size * k
    im = Image.new('RGBA', (s, s), CLEAR)
    d = ImageDraw.Draw(im)
    if shape == 'square':
        d.rectangle([0, 0, s, s], fill=background)
    elif shape == 'rounded':
        d.rounded_rectangle([0, 0, s - 1, s - 1], radius=int(s * 0.22), fill=background)
    elif shape == 'circle':
        d.ellipse([0, 0, s - 1, s - 1], fill=background)
    h = s * pin_height_share
    if h <= 0:
        im.resize((size, size), Image.LANCZOS).save(os.path.join(folder, name))
        return im
    pin(d, s / 2, (s - h) / 2, h, WHITE, background if background[3] else BLUE)
    im = im.resize((size, size), Image.LANCZOS)
    im.save(os.path.join(folder, name))
    return im


# Android adaptive icon: 108 dp layers, of which a 66 dp circle is always
# visible. The pin stays inside that circle.
render(432, BLUE, 'square', 0.0, 'adaptive-background.png')
fg = Image.new('RGBA', (432 * 4, 432 * 4), CLEAR)
d = ImageDraw.Draw(fg)
h = 432 * 4 * 0.42
pin(d, 432 * 2, (432 * 4 - h) / 2, h, WHITE, BLUE)
fg.resize((432, 432), Image.LANCZOS).save(os.path.join(ART, 'adaptive-foreground.png'))

# Launchers that do not do adaptive icons.
render(512, BLUE, 'rounded', 0.56, 'legacy.png')
render(512, BLUE, 'circle', 0.52, 'round.png')

# Play: 512 x 512, full-bleed square (Play rounds the corners itself).
render(512, BLUE, 'square', 0.56, 'play-icon-512.png', STORE)

# Play feature graphic, 1024 x 500.
W, H, k = 1024, 500, 2
fgimg = Image.new('RGBA', (W * k, H * k), BLUE)
d = ImageDraw.Draw(fgimg)
pin(d, 250 * k, 110 * k, 280 * k, WHITE, BLUE)
title = ImageFont.truetype(FONT, 92 * k)
sub = ImageFont.truetype(FONT_REG, 36 * k)
d.text((430 * k, 150 * k), 'AR Trail', font=title, fill=WHITE)
d.text((434 * k, 272 * k), 'Градски маршрути с добавена', font=sub, fill=(255, 255, 255, 225))
d.text((434 * k, 318 * k), 'реалност. Намери спирките.', font=sub, fill=(255, 255, 255, 225))
fgimg.convert('RGB').resize((W, H), Image.LANCZOS).save(os.path.join(STORE, 'feature-graphic-1024x500.png'))
print('ok', os.listdir(ART), os.listdir(STORE))
