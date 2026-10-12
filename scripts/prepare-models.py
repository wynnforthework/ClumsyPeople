"""Build-time only: download, verify and unpack the models shipped in the ZIP."""
import argparse
import hashlib
from pathlib import Path
import shutil
import tarfile
import tempfile
import urllib.request

MODELS = (
    ("asr-models", "sherpa-onnx-streaming-zipformer-zh-14M-2023-02-23",
     "2cbd71b640d9c37d3784f29367333a4577b0398b62e9deeed418170b081cba8b"),
    ("punctuation-models", "sherpa-onnx-punct-ct-transformer-zh-en-vocab272727-2024-04-12-int8",
     "c0d5aa5f8eeb686032345e180bedf39319dc2e0556781c6264bcadba8328a6e1"),
)


def prepare(destination: Path, archives: Path, proxy: str) -> None:
    destination = destination.resolve()
    destination.mkdir(parents=True, exist_ok=True)
    archives.mkdir(parents=True, exist_ok=True)
    opener = urllib.request.build_opener(
        urllib.request.ProxyHandler({"https": proxy}) if proxy else urllib.request.ProxyHandler())
    for tag, name, expected in MODELS:
        archive = archives / (name + ".tar.bz2")
        if not archive.exists():
            url = f"https://github.com/k2-fsa/sherpa-onnx/releases/download/{tag}/{name}.tar.bz2"
            partial = archive.with_suffix(archive.suffix + ".partial")
            try:
                with opener.open(url, timeout=120) as response, partial.open("wb") as output:
                    shutil.copyfileobj(response, output)
                partial.replace(archive)
            finally:
                partial.unlink(missing_ok=True)
        with archive.open("rb") as stream:
            actual = hashlib.file_digest(stream, "sha256").hexdigest()
        if actual != expected:
            raise ValueError(f"SHA256 mismatch: {archive}; model was not packaged")
        with tempfile.TemporaryDirectory(prefix=".model-prep-", dir=destination.parent) as staging:
            with tarfile.open(archive, "r:bz2") as source:
                for member in source.getmembers():
                    if Path(member.name).parts[0] != name:
                        raise ValueError(f"Unexpected model archive entry: {member.name}")
                source.extractall(staging, filter="data")
            target = destination / name
            if target.resolve().parent != destination:
                raise ValueError(f"Invalid model destination: {target}")
            if target.exists():
                shutil.rmtree(target)
            shutil.move(str(Path(staging) / name), str(target))
        print(f"Verified and extracted: {name}", flush=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--destination", type=Path, required=True)
    parser.add_argument("--archives", type=Path, default=Path(".tools/model-archives"))
    parser.add_argument("--proxy", default="")
    args = parser.parse_args()
    prepare(args.destination, args.archives, args.proxy)
