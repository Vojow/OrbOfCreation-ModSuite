#!/usr/bin/env python3
"""Start a detached loopback-only HTTP server for generated trace dashboards."""

import argparse
import functools
import http.server
import os
import pathlib
import sys


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", required=True)
    parser.add_argument("--port", required=True, type=int)
    parser.add_argument("--log", required=True)
    parser.add_argument("--pid-file", required=True)
    return parser.parse_args()


def daemonize(log_path: pathlib.Path, pid_path: pathlib.Path) -> bool:
    first = os.fork()
    if first:
        os.waitpid(first, 0)
        return False

    os.setsid()
    second = os.fork()
    if second:
        os._exit(0)

    log_path.parent.mkdir(parents=True, exist_ok=True)
    log = os.open(log_path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
    null = os.open(os.devnull, os.O_RDONLY)
    os.dup2(null, sys.stdin.fileno())
    os.dup2(log, sys.stdout.fileno())
    os.dup2(log, sys.stderr.fileno())
    os.close(null)
    os.close(log)

    temporary = pid_path.with_suffix(pid_path.suffix + ".tmp")
    temporary.write_text(f"{os.getpid()}\n", encoding="ascii")
    os.replace(temporary, pid_path)
    return True


def main() -> None:
    args = parse_args()
    root = pathlib.Path(args.root).resolve(strict=True)
    if not root.is_dir():
        raise SystemExit(f"Trace root is not a directory: {root}")
    if args.port < 1 or args.port > 65535:
        raise SystemExit("Port must be from 1 through 65535.")

    log_path = pathlib.Path(args.log).resolve()
    pid_path = pathlib.Path(args.pid_file).resolve()
    if not daemonize(log_path, pid_path):
        return

    handler = functools.partial(
        http.server.SimpleHTTPRequestHandler,
        directory=str(root),
    )
    server = http.server.ThreadingHTTPServer(("127.0.0.1", args.port), handler)
    server.serve_forever()


if __name__ == "__main__":
    main()
