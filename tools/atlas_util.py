"""Image helpers shared by the livery scripts (make_clean_livery.py, make_liveries.py)."""
import os
import numpy as np
from PIL import Image

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))
MASKS = os.path.join(ROOT, 'blender', 'out', 'livery_masks.png')   # tools/blender_livery_masks.py


def box(a, k, axis):
    """Box filter of odd width k along one axis, edges extended."""
    p = k // 2
    pad = [(0, 0)] * a.ndim; pad[axis] = (p + 1, p)
    c = np.cumsum(np.pad(a, pad, mode='edge'), axis=axis)
    return (np.take(c, range(k, c.shape[axis]), axis=axis) - np.take(c, range(0, c.shape[axis] - k), axis=axis)) / k


def blur(a, sigma):
    """Near-Gaussian blur of a float image (h, w[, c]): three box passes per axis."""
    k = max(1, int(round(np.sqrt(4 * sigma * sigma + 1)))) | 1
    for axis in (0, 1):
        for _ in range(3):
            a = box(a, k, axis)
    return a


def ramp(x, lo, hi):
    return np.clip((x - lo) / (hi - lo), 0.0, 1.0)


def masks(size):
    """The baked masks at the atlas size: (normal z -1..1, noise a 0..1, noise b 0..1, baked incl. the margin,
    covered by a face), or None before tools/blender_livery_masks.py has run."""
    if not os.path.exists(MASKS):
        return None
    m = np.asarray(Image.open(MASKS).convert('RGBA').resize((size, size), Image.BILINEAR)).astype(np.float32) / 255
    return m[..., 0] * 2 - 1, m[..., 1], m[..., 2], (m[..., 1] + m[..., 2]) > 0.02, m[..., 3] > 0.5

