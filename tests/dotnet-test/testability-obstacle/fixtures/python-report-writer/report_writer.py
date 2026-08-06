from datetime import datetime


def write_report(path: str, content: str) -> str:
    timestamped = f"{path}-{datetime.now():%Y%m%d-%H%M%S}.txt"
    with open(timestamped, "w", encoding="utf-8") as stream:
        stream.write(content)
    return timestamped
