import importlib.util, io, threading, unittest, urllib.request, urllib.error
from pathlib import Path
from unittest.mock import patch
spec=importlib.util.spec_from_file_location('relay',Path(__file__).parents[1]/'legacy_relay.py')
relay=importlib.util.module_from_spec(spec);spec.loader.exec_module(relay)
class RedirectContract(unittest.TestCase):
 def test_reject_foreign_and_unencrypted_redirects(self):
  for url in ['http://release-assets.githubusercontent.com/a','https://evil.invalid/a','https://release-assets.githubusercontent.com.evil.invalid/a','https://user@release-assets.githubusercontent.com/a']:
   with self.subTest(url=url),self.assertRaises(urllib.error.HTTPError):relay.SafeRedirect().redirect_request(urllib.request.Request('https://github.com/afonasev/star-racing/releases/download/v1/a'),None,302,'redirect',{},url)
 def test_allow_github_asset_redirect(self):
  result=relay.SafeRedirect().redirect_request(urllib.request.Request('https://github.com/afonasev/star-racing/releases/download/v1/a'),None,302,'redirect',{},'https://release-assets.githubusercontent.com/a?token=test')
  self.assertEqual(result.host,'release-assets.githubusercontent.com')
class RelayContract(unittest.TestCase):
 def setUp(self):
  self.client=urllib.request.build_opener()
  relay.CATALOG={'/known':{'url':'https://github.com/afonasev/star-racing/releases/download/v1/a','size':3}}
  self.server=relay.ThreadingHTTPServer(('127.0.0.1',0),relay.Handler);self.thread=threading.Thread(target=self.server.serve_forever);self.thread.start();self.base='http://127.0.0.1:'+str(self.server.server_port)
 def tearDown(self):self.server.shutdown();self.thread.join();self.server.server_close()
 def test_unknown_path_has_no_upstream(self):
  with patch.object(relay.urllib.request,'build_opener') as opener,self.assertRaises(urllib.error.HTTPError) as error:self.client.open(self.base+'/unknown?url=https://evil.invalid')
  self.assertEqual(error.exception.code,404);opener.assert_not_called()
 def test_valid_stream_and_head(self):
  class Response(io.BytesIO):status=200;headers={'Content-Length':'3'}
  for method in ['GET','HEAD']:
   with patch.object(relay.urllib.request,'build_opener') as opener:
    opener.return_value.open.return_value=Response(b'abc')
    with self.client.open(urllib.request.Request(self.base+'/known',method=method)) as result:
     self.assertEqual(result.headers['Content-Length'],'3');self.assertEqual(result.read(),b'abc' if method=='GET' else b'')
 def test_upstream_size_failure_is_502(self):
  class Response(io.BytesIO):status=200;headers={'Content-Length':'4'}
  with patch.object(relay.urllib.request,'build_opener') as opener,self.assertRaises(urllib.error.HTTPError) as error:
   opener.return_value.open.return_value=Response(b'abcd');self.client.open(self.base+'/known')
  self.assertEqual(error.exception.code,502)
