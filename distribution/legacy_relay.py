#!/usr/bin/env python3
"""No-disk compatibility relay for old Players which disallow HTTP redirects.

Only exact, hash-verified published package paths in the deployment catalog exist.
New Players download directly from GitHub; signed metadata remains on the origin.
"""
import json, sys, urllib.request, urllib.error
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlsplit
CATALOG=json.load(open(sys.argv[1])) if __name__=='__main__' else {}
class SafeRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self,req,fp,code,msg,headers,newurl):
        url=urlsplit(newurl)
        if url.scheme!='https' or url.hostname!='release-assets.githubusercontent.com' or url.port not in (None,443) or url.username:
            raise urllib.error.HTTPError(newurl,502,'Invalid GitHub redirect',headers,fp)
        return super().redirect_request(req,fp,code,msg,headers,newurl)
class Handler(BaseHTTPRequestHandler):
    def do_HEAD(self):self.serve(False)
    def do_GET(self):self.serve(True)
    def serve(self,body):
        asset=CATALOG.get(self.path)
        if not asset:self.send_error(404);return
        headers_sent=False
        try:
            opener=urllib.request.build_opener(SafeRedirect())
            with opener.open(urllib.request.Request(asset['url'],headers={'User-Agent':'Star-Racing-legacy-relay'}),timeout=30) as response:
                if response.status!=200 or int(response.headers.get('Content-Length',-1))!=asset['size']:raise ValueError('Upstream size mismatch')
                self.send_response(200);self.send_header('Content-Length',str(asset['size']));self.send_header('Content-Type','application/octet-stream');self.send_header('Cache-Control','public, max-age=31536000, immutable');self.end_headers();headers_sent=True
                if body:
                    remaining=asset['size']
                    while remaining:
                        chunk=response.read(min(65536,remaining))
                        if not chunk:raise ValueError('Truncated upstream')
                        self.wfile.write(chunk);remaining-=len(chunk)
        except (BrokenPipeError,ConnectionResetError):pass
        except Exception as error:
            self.log_error('upstream error: %s',type(error).__name__)
            if not headers_sent:self.send_error(502)
            self.close_connection=True
if __name__=='__main__':ThreadingHTTPServer(('127.0.0.1',41872),Handler).serve_forever()
