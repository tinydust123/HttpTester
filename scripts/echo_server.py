"""本地回显服务：python scripts/echo_server.py，只监听本机。"""
import json
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qsl, urlsplit


class EchoHandler(BaseHTTPRequestHandler):
    def echo(self):
        length = int(self.headers.get("Content-Length", "0"))
        if length > 10 * 1024 * 1024:
            self.send_error(413, "Demo server accepts at most 10 MB")
            return
        body = self.rfile.read(length).decode("utf-8", errors="replace")
        result = {
            "message": "你好，Http探测器！",
            "method": self.command,
            "path": self.path,
            "query": parse_qsl(urlsplit(self.path).query, keep_blank_values=True),
            "headers": dict(self.headers),
            "body": body,
        }
        data = json.dumps(result, ensure_ascii=False, indent=2).encode("utf-8")
        self.send_response(200)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(data)

    do_GET = do_POST = do_PUT = do_PATCH = do_DELETE = do_HEAD = do_OPTIONS = do_TRACE = echo


if __name__ == "__main__":
    print("本地回显服务：http://localhost:8765/echo；按 Ctrl+C 停止。", flush=True)
    try:
        ThreadingHTTPServer(("localhost", 8765), EchoHandler).serve_forever()
    except KeyboardInterrupt:
        pass
