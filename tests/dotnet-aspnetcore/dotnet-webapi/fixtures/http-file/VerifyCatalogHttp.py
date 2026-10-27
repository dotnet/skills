import json
import re
import sys
from pathlib import Path


files = list(Path(".").glob("*.http"))
text = "\n".join(path.read_text() for path in files)
declarations = re.findall(r"(?im)^@([A-Za-z_]\w*)\s*=", text)
variable = declarations[0] if declarations and len(set(declarations)) == 1 else None
if not files or not variable:
    sys.exit(1)

request_pattern = re.compile(
    r"(?im)^(GET|POST|DELETE)\s+\{\{"
    + re.escape(variable)
    + r"\}\}(/api/catalog(?:/\d+)?)(?:\s+HTTP/\d(?:\.\d)?)?\s*$"
)
blocks = re.split(r"(?m)^\s*###(?:\s+.*)?$", text)
requests = []
for block in blocks:
    matches = list(request_pattern.finditer(block))
    if not matches:
        continue
    if len(matches) != 1:
        sys.exit(1)
    match = matches[0]
    requests.append((match.group(1).upper(), match.group(2), block, match.end()))

collection_gets = [request for request in requests if request[:2] == ("GET", "/api/catalog")]
item_gets = [request for request in requests if request[0] == "GET" and request[1].startswith("/api/catalog/")]
posts = [request for request in requests if request[:2] == ("POST", "/api/catalog")]
deletes = [request for request in requests if request[0] == "DELETE" and request[1].startswith("/api/catalog/")]
ids = [request[1].rsplit("/", 1)[-1] for request in item_gets + deletes]

post_valid = False
if len(posts) == 1:
    post_remainder = posts[0][2][posts[0][3]:]
    parts = re.split(r"\r?\n\s*\r?\n", post_remainder, maxsplit=1)
    if len(parts) == 2 and re.search(r"(?im)^Content-Type:\s*application/json\s*$", parts[0]):
        try:
            body = json.loads(parts[1].strip())
            post_valid = (
                isinstance(body, dict)
                and isinstance(body.get("name"), str)
                and bool(body["name"].strip())
                and isinstance(body.get("price"), (int, float))
                and not isinstance(body["price"], bool)
            )
        except json.JSONDecodeError:
            pass

valid = (
    len(collection_gets) == 1
    and len(item_gets) >= 1
    and len(deletes) >= 1
    and post_valid
    and any(value != "1" for value in ids)
)
sys.exit(0 if valid else 1)
