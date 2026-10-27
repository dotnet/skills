import os
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


def reserve_port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


project = next(Path(".").glob("*.csproj"))
port = reserve_port()
base_url = f"http://127.0.0.1:{port}"
environment = os.environ.copy()
environment["FEATURED_PRODUCTS_FAIL"] = "1"
process = subprocess.Popen(
    [
        "dotnet",
        "run",
        "--no-build",
        "--project",
        str(project),
        "--urls",
        base_url,
    ],
    env=environment,
    stdout=subprocess.PIPE,
    stderr=subprocess.STDOUT,
    text=True,
)

try:
    deadline = time.monotonic() + 30
    while True:
        try:
            with urllib.request.urlopen(base_url + "/featured-products", timeout=5) as response:
                status = response.status
                body = response.read().decode()
            break
        except urllib.error.HTTPError as error:
            status = error.code
            body = error.read().decode()
            break
        except (OSError, urllib.error.URLError):
            if process.poll() is not None:
                print(process.stdout.read(), file=sys.stderr)
                sys.exit(process.returncode or 1)
            if time.monotonic() >= deadline:
                print("Timed out waiting for the Blazor app to start.", file=sys.stderr)
                sys.exit(1)
            time.sleep(0.25)

    assert status == 200, status
    assert "Unable to load featured products." in body
    assert "FEATURED-PRODUCTS-SENSITIVE" not in body
finally:
    process.terminate()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=10)
