import os,subprocess,time,json,urllib.request,pathlib
root=pathlib.Path('.').resolve();(root/'work').mkdir(exist_ok=True); reports=root/'reports'; assembly=root/'publish/SsmsAiSqlAssistant.LocalService.dll';records=[]
def start(kind):
 env=os.environ.copy();env.update(SSMS_AI_SQL_DATA_DIR=str(root/'work/startup-data'),OPENAI_API_KEY='',OPENAI_API_KEY_FILE=str(root/('work/mock-test.key' if kind=='file' else 'work/absent-test.key')),OPENAI_MODEL='mock-startup-model',SSMS_AI_URLS='http://127.0.0.1:46218')
 return subprocess.Popen(['dotnet',str(assembly)],env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,encoding='utf-8',errors='replace')
def health():
 for i in range(100):
  try:return json.load(urllib.request.urlopen('http://127.0.0.1:46218/health',timeout=.5))
  except Exception:time.sleep(.1)
 raise RuntimeError('Startup timeout')
p=start('missing')
try:
 h=health();records.append(dict(case='missing-key-file',passed=h['aiProvider']=='local-rules' and h['apiKeySource']=='configured-file-unreadable',health=h))
 conflict=start('missing');out,_=conflict.communicate(timeout=15);records.append(dict(case='port-conflict',passed=conflict.returncode!=0,exitCode=conflict.returncode,log=out))
finally:p.terminate();out,_=p.communicate(timeout=10)
(root/'work/mock-test.key').write_text('synthetic-test-key-only',encoding='utf-8')
p=start('file')
try:
 h=health();records.append(dict(case='key-file-health',passed=h['aiProvider']=='openai' and h['apiKeySource']=='configured-file' and 'synthetic-test-key-only' not in json.dumps(h),health=h))
finally:
 p.terminate();out,_=p.communicate(timeout=10);(root/'work/mock-test.key').unlink()
records.append(dict(case='console-privacy',passed='synthetic-test-key-only' not in out,log=out))
reports.joinpath('startup-verification.json').write_text(json.dumps(records,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'passed':sum(x['passed'] for x in records),'total':len(records)}))

