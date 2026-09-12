import sys,pathlib,json,urllib.request

from openapi_spec_validator import validate_spec
from jsonschema import Draft202012Validator
p=pathlib.Path('openapi.json');spec=json.loads(p.read_text(encoding='utf-8'));validate_spec(spec)
checks=[]
for path in ['/health','/api/v1/capabilities','/metadata-query','/evidence-script','/agent/stored-procedures/query','/agent/stored-procedures/query-store-summary-query','/agent/stored-procedures/parameter-evidence-query','/history','/history/stats','/usage','/agent/jobs','/agent/memory','/agent/schedules','/agent/monitor/events','/sp/change-audit','/sp/change-audit/verify']:
 data=json.load(urllib.request.urlopen('http://127.0.0.1:46217'+path))
 schema=spec['paths'][path]['get']['responses']['200']['content']['application/json']['schema'].copy();schema['components']=spec['components'];Draft202012Validator(schema).validate(data);checks.append({'path':path,'passed':True})
count=sum(len([m for m in v if m in ['get','post','delete']]) for v in spec['paths'].values())
pathlib.Path('reports/openapi-verification.json').write_text(json.dumps({'openapi31Valid':True,'operationCount':count,'schemaCount':len(spec['components']['schemas']),'liveResponseChecks':checks},indent=2),encoding='utf-8')
print(json.dumps({'operations':count,'schemas':len(spec['components']['schemas']),'liveResponsesValidated':len(checks)}))

