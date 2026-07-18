#!/usr/bin/env python3
"""Local static server for the Radiant Pool WebGL build (webbase/game).

Unity ships Brotli-compressed files (*.br). Any static server works because the
build uses decompression fallback, but serving the correct Content-Encoding lets
the browser decompress natively — noticeably faster first load. Usage:

    python serve.py [port]     # default 8080, serves webbase/ then open /game/
"""
import sys
import http.server
import functools
from pathlib import Path

ROOT = Path(__file__).parent


class UnityHandler(http.server.SimpleHTTPRequestHandler):
    def end_headers(self):
        path = self.path.split("?")[0]
        # .unityweb = Brotli under decompression-fallback naming; the header lets the
        # browser decompress natively instead of the loader's JS fallback.
        if path.endswith((".br", ".unityweb")):
            self.send_header("Content-Encoding", "br")
        elif path.endswith(".gz"):
            self.send_header("Content-Encoding", "gzip")
        inner = path[:-3] if path.endswith((".br", ".gz")) else path
        if inner.endswith(".wasm"):
            self.send_header("Content-Type", "application/wasm")
        elif inner.endswith(".js"):
            self.send_header("Content-Type", "application/javascript")
        elif inner.endswith(".data"):
            self.send_header("Content-Type", "application/octet-stream")
        super().end_headers()


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 8080
    handler = functools.partial(UnityHandler, directory=str(ROOT))
    print(f"Radiant Pool -> http://localhost:{port}/game/  (Ctrl+C stops)")
    http.server.ThreadingHTTPServer(("127.0.0.1", port), handler).serve_forever()


if __name__ == "__main__":
    main()
