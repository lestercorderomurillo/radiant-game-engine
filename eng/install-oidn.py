#!/usr/bin/env python3
"""Install the official optional OIDN runtime locally. Binaries remain outside version control."""
import hashlib
import json
import pathlib
import platform
import shutil
import tarfile
import tempfile
import urllib.request
import zipfile

VERSION = "2.5.1"
ROOT = pathlib.Path(__file__).resolve().parents[1]
system, machine = platform.system(), platform.machine().lower()
suffix = {("Linux", "x86_64"): "x86_64.linux.tar.gz", ("Windows", "amd64"): "x64.windows.zip", ("Darwin", "arm64"): "arm64.macos.tar.gz", ("Darwin", "x86_64"): "x86_64.macos.tar.gz"}.get((system, machine))
if suffix is None:
    raise SystemExit(f"No official OIDN {VERSION} archive configured for {system}/{machine}.")
with urllib.request.urlopen(f"https://api.github.com/repos/RenderKit/oidn/releases/tags/v{VERSION}") as response:
    release = json.load(response)
asset = next(a for a in release["assets"] if a["name"] == f"oidn-{VERSION}.{suffix}")
digest = asset.get("digest", "")
if not digest.startswith("sha256:"):
    raise SystemExit("Official release metadata has no SHA-256 digest; refusing an unverified runtime.")
with tempfile.TemporaryDirectory(prefix="radiant-oidn-") as temporary:
    base = pathlib.Path(temporary)
    archive = base / asset["name"]
    print(f"Downloading official OIDN {VERSION}: {asset['name']}", flush=True)
    urllib.request.urlretrieve(asset["browser_download_url"], archive)
    with archive.open("rb") as downloaded:
        if hashlib.file_digest(downloaded, "sha256").hexdigest() != digest.removeprefix("sha256:"):
            raise SystemExit("OIDN archive checksum mismatch.")
    extracted = base / "unpacked"
    extracted.mkdir()
    if suffix.endswith(".zip"):
        with zipfile.ZipFile(archive) as package:
            for name in package.namelist():
                if not (extracted / name).resolve().is_relative_to(extracted):
                    raise SystemExit("Invalid archive member.")
            package.extractall(extracted)
    else:
        with tarfile.open(archive) as package:
            package.extractall(extracted, filter="data")
    directory = next(extracted.iterdir())
    destination = ROOT / "Native" / "oidn"
    destination.mkdir(parents=True, exist_ok=True)
    for item in directory.iterdir():
        if item.name in ("lib", "bin", "doc", "share") or item.name.lower().startswith(("license", "third", "notice")):
            if item.is_dir():
                shutil.copytree(item, destination / item.name, dirs_exist_ok=True)
            else:
                shutil.copy2(item, destination / item.name)
    (destination / "VERSION.txt").write_text(f"OIDN {VERSION}\n{asset['browser_download_url']}\n{digest}\n")
    print(f"Installed {destination}. Rebuild the application to copy its optional runtime.")
