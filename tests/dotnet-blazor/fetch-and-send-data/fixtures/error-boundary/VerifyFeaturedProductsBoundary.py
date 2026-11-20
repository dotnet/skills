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


def render_page(should_fail):
    project = next(Path(".").glob("*.csproj"))
    port = reserve_port()
    base_url = f"http://127.0.0.1:{port}"
    environment = os.environ.copy()
    environment["FEATURED_PRODUCTS_FAIL"] = "1" if should_fail else "0"
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
                    return response.status, response.read().decode()
            except urllib.error.HTTPError as error:
                return error.code, error.read().decode()
            except (OSError, urllib.error.URLError):
                if process.poll() is not None:
                    print(process.stdout.read(), file=sys.stderr)
                    sys.exit(process.returncode or 1)
                if time.monotonic() >= deadline:
                    print("Timed out waiting for the Blazor app to start.", file=sys.stderr)
                    sys.exit(1)
                time.sleep(0.25)
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)


failure_status, failure_body = render_page(should_fail=True)
assert failure_status == 200, failure_status
assert "Unable to load featured products." in failure_body
assert "FEATURED-PRODUCTS-SENSITIVE" not in failure_body

success_status, success_body = render_page(should_fail=False)
assert success_status == 200, success_status
assert "Keyboard" in success_body
assert "Monitor" in success_body
assert "Unable to load featured products." not in success_body
