import json
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request


def reserve_port():
    with socket.socket() as listener:
        listener.bind(("127.0.0.1", 0))
        return listener.getsockname()[1]


def get_problem(base_url, path):
    try:
        with urllib.request.urlopen(base_url + path, timeout=5) as response:
            status = response.status
            content_type = response.headers.get_content_type()
            body = response.read().decode()
    except urllib.error.HTTPError as error:
        status = error.code
        content_type = error.headers.get_content_type()
        body = error.read().decode()

    return status, content_type, json.loads(body)


def wait_for_server(base_url, process):
    deadline = time.monotonic() + 30
    while True:
        try:
            urllib.request.urlopen(base_url + "/api/items/1", timeout=2)
            return
        except urllib.error.HTTPError:
            return
        except (OSError, urllib.error.URLError):
            if process.poll() is not None:
                print(process.stdout.read(), file=sys.stderr)
                sys.exit(process.returncode or 1)
            if time.monotonic() >= deadline:
                print("Timed out waiting for the API to start.", file=sys.stderr)
                sys.exit(1)
            time.sleep(0.25)


port = reserve_port()
base_url = f"http://127.0.0.1:{port}"
process = subprocess.Popen(
    [
        "dotnet",
        "run",
        "--no-build",
        "--project",
        "ExceptionApi.csproj",
        "--urls",
        base_url,
    ],
    stdout=subprocess.PIPE,
    stderr=subprocess.STDOUT,
    text=True,
)

try:
    wait_for_server(base_url, process)

    cases = [
        ("/api/items/0", 400),
        ("/api/items/1", 404),
        ("/api/conflict", 409),
        ("/api/failure", 500),
    ]
    for path, expected_status in cases:
        status, content_type, problem = get_problem(base_url, path)
        assert status == expected_status, (path, status, expected_status)
        assert content_type == "application/problem+json", (path, content_type)
        assert problem["status"] == expected_status, (path, problem)
        assert problem.get("title"), (path, problem)

    _, _, failure = get_problem(base_url, "/api/failure")
    serialized_failure = json.dumps(failure)
    assert "TOP-SECRET-FAILURE" not in serialized_failure
    assert "stack" not in serialized_failure.lower()
finally:
    process.terminate()
    try:
        process.wait(timeout=10)
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=10)
