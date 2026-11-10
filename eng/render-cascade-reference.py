#!/usr/bin/env python3
"""Independent Mitsuba 3 path reference for --cascade-reference's exact scene export.
Install mitsuba==3.9.1 numpy pillow in a venv. Images use identical linear clipping;
EXR/NPY retain HDR radiance. No denoising, clamping, reconstruction or depth cutoff.
"""
import argparse
import json
import hashlib
from pathlib import Path
import mitsuba as mi
import numpy as np
from PIL import Image

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--directory', type=Path, default=Path('artifacts/radiance-reference'))
parser.add_argument('--spp', type=int, default=4096)
parser.add_argument('--batch', type=int, default=32)
parser.add_argument('--compare-only', action='store_true', help='reuse the saved reference without rerendering')
parser.add_argument('--check-improvement', action='store_true', help='require lower MAE than cascade-before.f32')
args = parser.parse_args()
if args.spp < 1 or args.batch < 1:
    parser.error('sample counts must be positive')
scene_bytes = (args.directory / 'scene.json').read_bytes()
scene_hash = hashlib.sha256(scene_bytes).hexdigest()
data = json.loads(scene_bytes)
if not args.compare_only:
    mi.set_variant('llvm_ad_rgb')
    position = np.array(data['position'])
    scene = {'type': 'scene', 'integrator': {'type': 'path', 'max_depth': -1},
             'environment': {'type': 'constant', 'radiance': {'type': 'rgb', 'value': data['environment']}},
             'sensor': {'type': 'perspective', 'fov': data['fov'], 'fov_axis': 'y',
                        'near_clip': 0.1, 'far_clip': 40,
                        'to_world': mi.ScalarTransform4f().look_at(origin=position, target=position + data['forward'], up=data['up']),
                        'sampler': {'type': 'independent'},
                        'film': {'type': 'hdrfilm', 'width': data['width'], 'height': data['height'],
                                 'rfilter': {'type': 'box'}, 'pixel_format': 'rgb'}}}
    # Exported triangles retain exact world positions and linear material values.
    groups = {}
    for triangle in data['triangles']:
        key = tuple(triangle['albedo'] + triangle['emission'])
        groups.setdefault(key, []).append(triangle)
    for index, (material, triangles) in enumerate(groups.items()):
        filename = args.directory / f'mesh-{index}.ply'
        vertices = [t[v] for t in triangles for v in ('a', 'b', 'c')]
        with filename.open('w') as out:
            out.write(f'ply\nformat ascii 1.0\nelement vertex {len(vertices)}\nproperty float x\nproperty float y\nproperty float z\nelement face {len(triangles)}\nproperty list uchar int vertex_indices\nend_header\n')
            for vertex in vertices:
                out.write(' '.join(map(str, vertex)) + '\n')
            for face in range(len(triangles)):
                out.write(f'3 {3*face} {3*face+1} {3*face+2}\n')
        shape = {'type': 'ply', 'filename': str(filename.resolve()),
                 'bsdf': {'type': 'twosided', 'material': {'type': 'diffuse', 'reflectance': {'type': 'rgb', 'value': material[:3]}}}}
        if max(material[3:]) > 0:
            shape['emitter'] = {'type': 'area', 'radiance': {'type': 'rgb', 'value': material[3:]}}
        scene[f'mesh{index}'] = shape
    scene = mi.load_dict(scene)
    sums = [np.zeros((data['height'], data['width'], 3), dtype=np.float64) for _ in range(2)]
    counts = [0, 0]
    for batch, start in enumerate(range(0, args.spp, args.batch)):
        samples = min(args.batch, args.spp - start)
        rendered = np.array(mi.render(scene, seed=batch + 1, spp=samples))
        sums[batch % 2] += rendered * samples
        counts[batch % 2] += samples
        if batch % 8 == 0:
            print(f'{start + samples}/{args.spp} spp', flush=True)
    reference = ((sums[0] + sums[1]) / sum(counts)).astype(np.float32)
    np.save(args.directory / 'reference.npy', reference)
    mi.Bitmap(reference).write(str(args.directory / 'reference.exr'))
    Image.fromarray(np.uint8(np.clip(reference, 0, 1) * 255 + 0.5)).save(args.directory / 'reference.png')
else:
    reference = np.load(args.directory / 'reference.npy')
cascade = np.fromfile(args.directory / 'cascade.f32', dtype='<f4').reshape(reference.shape)
if not np.isfinite(reference).all() or not np.isfinite(cascade).all():
    raise SystemExit('Non-finite radiance in comparison')
error = np.abs(cascade - reference)
if args.compare_only:
    metrics = json.loads((args.directory / 'metrics.json').read_text())
    if metrics['scene_sha256'] != scene_hash:
        raise SystemExit('Scene export differs from the saved reference; rerender it')
else:
    metrics = {'renderer': mi.__version__, 'spp': args.spp, 'max_depth': -1,
               'resolution': [data['width'], data['height']], 'scene_sha256': scene_hash}
    if min(counts) > 0:
        metrics['independent_half_rmse'] = float(np.sqrt(np.mean((sums[0]/counts[0] - sums[1]/counts[1])**2)))
metrics.update(linear_mae=float(error.mean()), linear_rmse=float(np.sqrt(np.mean(error ** 2))))
metrics.pop('baseline_mae', None)
metrics.pop('mae_reduction_percent', None)
baseline_path = args.directory / 'cascade-before.f32'
if baseline_path.exists():
    baseline = np.fromfile(baseline_path, dtype='<f4').reshape(reference.shape)
    metrics['baseline_mae'] = float(np.abs(baseline - reference).mean())
    metrics['mae_reduction_percent'] = 100 * (1 - metrics['linear_mae'] / metrics['baseline_mae'])
if args.check_improvement and (metrics['spp'] < 4096 or metrics.get('mae_reduction_percent', 0) <= 0):
    raise SystemExit('Comparison requires a >=4096 spp reference and improved MAE over cascade-before.f32')
(args.directory / 'metrics.json').write_text(json.dumps(metrics, indent=2) + '\n')
Image.fromarray(np.uint8(np.clip(error * 4, 0, 1) * 255)).save(args.directory / 'error-x4.png')
print(json.dumps(metrics, indent=2), flush=True)
comparison = np.concatenate([cascade, reference, np.minimum(error * 4, 1)], axis=1)
Image.fromarray(np.uint8(np.clip(comparison, 0, 1) * 255 + 0.5)).save(args.directory / 'comparison.png')
