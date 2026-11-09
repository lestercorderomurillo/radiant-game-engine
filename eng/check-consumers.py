#!/usr/bin/env python3
"""Build Radiant and its sibling games, then run their deterministic suites."""
import argparse
from pathlib import Path
import subprocess


def main():
    engine = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--configuration', default='Release', choices=('Debug', 'Release'))
    parser.add_argument('--pacman-root', type=Path, default=engine.parent / 'raytracing-pacman')
    parser.add_argument('--zuma-root', type=Path, default=engine.parent / 'raytracing-zuma')
    args = parser.parse_args()
    projects = [
        (engine, 'radiant.sln', 'Radiant.Tests'),
        (args.pacman_root.resolve(), 'tests/Pacman.Tests/Pacman.Tests.csproj', 'Pacman.Tests'),
        (args.zuma_root.resolve(), 'tests/Zuma.Tests/Zuma.Tests.csproj', 'Zuma.Tests'),
    ]
    for root, solution, _ in projects:
        if not (root / solution).is_file():
            parser.error(f'Missing {root / solution}; clone the sibling game or supply its root option.')
    # Shared engine/content outputs must not be built concurrently.
    for root, solution, test in projects:
        print(f'Checking {solution} ({args.configuration})', flush=True)
        subprocess.run(['dotnet', 'build', solution, '-c', args.configuration, '-m:1',
                        f'-p:RadiantRoot={engine}'], cwd=root, check=True)
        assembly = root / 'tests' / test / 'bin' / args.configuration / 'net8.0' / f'{test}.dll'
        subprocess.run(['dotnet', str(assembly)], cwd=root, check=True)
    print('PASS: all eight Radiant/game projects build; all three deterministic suites pass.')
    print('Graphical/input checks are separate; see each game\'s BUILD.md and use an isolated display.')


if __name__ == '__main__':
    main()
