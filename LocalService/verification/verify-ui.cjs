const fs=require('fs');
const {chromium}=require('playwright');
(async()=>{
 const base='http://127.0.0.1:46217'; const root='reports'; fs.mkdirSync(root+'/screenshots',{recursive:true});
 const api=[];
 for(const path of ['/health','/api/v1/capabilities','/metadata-query','/evidence-script','/agent/stored-procedures/query','/agent/stored-procedures/query-store-summary-query','/agent/stored-procedures/parameter-evidence-query','/history','/usage','/agent/jobs','/agent/memory','/agent/schedules','/agent/monitor/events','/sp/change-audit/verify']){let r=await fetch(base+path);api.push({method:'GET',path,status:r.status,pass:r.ok});}
 const crypto=require('crypto');const hash=x=>crypto.createHash('sha256').update(x).digest('hex');
 const r=await fetch(base+'/analyze',{method:'POST',headers:{'content-type':'application/json'},body:JSON.stringify({selectedSql:'SELECT 1 AS value',metadata:[],estimatedPlan:{estimatedCost:0,estimatedRows:1,scanCount:0},serverFingerprint:hash('acceptance-server'),databaseFingerprint:hash('acceptance-db')})});
 const result=await r.json();api.push({method:'POST',path:'/analyze',status:r.status,pass:r.ok&&result.aiProvider==='local-rules'&&result.candidates.length===0});
 fs.writeFileSync(root+'/api-verification.json',JSON.stringify(api,null,2));
 const spec=await(await fetch(base+'/openapi.json')).json();fs.writeFileSync('openapi.json',JSON.stringify(spec,null,2));
 const browser=await chromium.launch({headless:true});const checks=[];
 for(const [name,path] of [['history','/history/ui'],['usage','/usage/ui'],['jobs','/jobs/ui'],['governance','/agent/governance/ui']]){
  for(const [width,height] of [[1024,768],[1366,768],[1920,1080]])for(const scale of [1,1.25,1.5]){
   const context=await browser.newContext({viewport:{width,height},deviceScaleFactor:scale});const page=await context.newPage();await page.goto(base+path);
   const layout=await page.evaluate(()=>({viewport:innerWidth,width:document.documentElement.scrollWidth,title:document.querySelector('h1').textContent,background:getComputedStyle(document.body).backgroundColor,tableScroll:getComputedStyle(document.querySelector('.table')).overflowX}));
   const file=`${name}-${width}x${height}-${scale}.png`;await page.screenshot({path:root+'/screenshots/'+file,fullPage:true});checks.push({name,width,height,deviceScaleFactor:scale,file,pass:layout.width<=layout.viewport,...layout});await context.close();
  }
 }
 await browser.close();fs.writeFileSync(root+'/layout-verification.json',JSON.stringify({checks,goldenComparison:'NOT VERIFIED: supplied catalog has no localhost golden screenshots',scaling:'Chromium deviceScaleFactor emulation, not Windows display scaling'},null,2));
 console.log(JSON.stringify({apiPassed:api.filter(x=>x.pass).length,apiTotal:api.length,layoutPassed:checks.filter(x=>x.pass).length,layoutTotal:checks.length}));
})().catch(e=>{console.error(e);process.exit(1)});
