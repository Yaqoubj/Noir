"""Range-capable synthetic media server. /lan/file and /debrid/file.
Debrid adds 40 ms before response headers and caps the aggregate response-body
rate at 25,000,000 bit/s. No TLS/DNS/auth/CDN simulation. Only serves media/.
"""
import argparse, json, mimetypes, pathlib, re, threading, time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import unquote, urlsplit
ROOT=pathlib.Path(__file__).resolve().parent
RATE=25_000_000/8
class Limiter:
    def __init__(self): self.lock=threading.Lock(); self.next=0.
    def wait(self,n):
        with self.lock:
            now=time.monotonic(); start=max(now-.010,self.next); self.next=start+n/RATE; deadline=self.next
        time.sleep(max(0,deadline-time.monotonic()))
limiter=Limiter()
class Handler(BaseHTTPRequestHandler):
    protocol_version='HTTP/1.1'
    def log_message(self, fmt, *args): pass
    def do_HEAD(self): self.serve(False)
    def do_GET(self): self.serve(True)
    def serve(self,body):
        start=time.monotonic(); path=unquote(urlsplit(self.path).path).split('/')
        if len(path)!=3 or path[1] not in ('lan','debrid') or not path[2] or pathlib.Path(path[2]).name!=path[2]: return self.send_error(404)
        f=(MEDIA/path[2]).resolve()
        if f.parent!=(MEDIA).resolve() or not f.is_file(): return self.send_error(404)
        size=f.stat().st_size; begin=0; end=size-1; status=200
        raw=self.headers.get('Range')
        if raw:
            match=re.fullmatch(r'bytes=(\d*)-(\d*)',raw)
            if not match or not any(match.groups()): return self.send_error(400,'Only single byte ranges are supported')
            a,b=match.groups()
            if a: begin=int(a); end=min(int(b),size-1) if b else size-1
            else: begin=max(0,size-int(b))
            if begin>=size or end<begin:
                self.send_response(416); self.send_header('Content-Range',f'bytes */{size}'); self.send_header('Content-Length','0'); self.end_headers(); return
            status=206
        if path[1]=='debrid': time.sleep(.040)
        self.send_response(status); self.send_header('Accept-Ranges','bytes'); self.send_header('Content-Type',mimetypes.guess_type(f.name)[0] or 'application/octet-stream')
        self.send_header('Content-Length',str(end-begin+1)); self.send_header('Cache-Control','no-store')
        if status==206: self.send_header('Content-Range',f'bytes {begin}-{end}/{size}')
        self.end_headers(); sent=0
        try:
            if body:
                with f.open('rb') as stream:
                    stream.seek(begin); remaining=end-begin+1
                    while remaining:
                        chunk=stream.read(min(32768,remaining))
                        if not chunk: break
                        if path[1]=='debrid': limiter.wait(len(chunk))
                        self.wfile.write(chunk); sent+=len(chunk); remaining-=len(chunk)
        except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError): pass
        finally:
            with self.server.loglock:
                self.server.requestlog.write(json.dumps({'time':time.time(),'path':self.path,'range':raw,'status':status,'start':begin,'end':end,'bytes_sent':sent,'elapsed_ms':round((time.monotonic()-start)*1000,2)})+'\n'); self.server.requestlog.flush()

def main():
    parser=argparse.ArgumentParser(); parser.add_argument('media_dir',type=pathlib.Path); parser.add_argument('--port',type=int,default=18765); args=parser.parse_args()
    global ROOT
    repo=pathlib.Path(__file__).resolve().parents[2]
    if not any(args.media_dir.resolve().is_relative_to(repo/folder) for folder in ("work","NoirValidation")): raise SystemExit("Media directory must be under ignored work/ or NoirValidation/")
    ROOT=args.media_dir.resolve().parent
    global MEDIA
    MEDIA=args.media_dir.resolve()
    (ROOT/'runtime').mkdir(exist_ok=True)
    with (ROOT/'runtime/http-requests.jsonl').open('a',encoding='utf-8') as log:
        server=ThreadingHTTPServer(('127.0.0.1',args.port),Handler); server.daemon_threads=True; server.loglock=threading.Lock(); server.requestlog=log
        print('TV speed server port',args.port,flush=True)
        try: server.serve_forever()
        except KeyboardInterrupt: pass
        finally: server.server_close()
if __name__=='__main__': main()

