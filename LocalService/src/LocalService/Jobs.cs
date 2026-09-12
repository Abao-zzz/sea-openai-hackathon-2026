namespace LocalService;
public sealed class Jobs(Store store,TimeProvider time)
{
    public JobRecord Create(JobCreate request,bool demo=false)
    {
        Contract.Check(request);Contract.Fingerprint(request.DatabaseFingerprint);
        if(request.ObjectIds.Length>request.TopN || request.ObjectIds.Any(x=>x<=0) || request.ObjectIds.Distinct().Count()!=request.ObjectIds.Length)throw new ApiError(400,"invalid_items","工作項目必須唯一且不得超出 Top N。");
        var j=new JobRecord(Contract.Id(),time.GetUtcNow(),request.DatabaseFingerprint,request.TopN,request.Budget,request.MaxRetries,"pending",false,null,null,null,null,request.ObjectIds.Select(x=>new JobItem(x,"pending",0,0,0,0,0,0)).ToArray(),demo);return store.Add("jobs",j.Id,j);
    }
    public JobRecord Get(string id) { var job=store.Get<JobRecord>("jobs",id);if(job.Status=="running" && job.StartedAt is not null && (time.GetUtcNow()-job.StartedAt.Value).TotalSeconds>job.Budget.TimeSeconds)return store.Update<JobRecord>("jobs",id,j=>j.Status=="running"?j with {Status="budget-exhausted",CancelRequested=true}:j);return job; }
    public JobRecord[] List()=>store.List<JobRecord>("jobs").Select(j=>Get(j.Id)).ToArray();
    private void Active(JobRecord j,JobCommand c)
    {
        if(j.DatabaseFingerprint!=c.DatabaseFingerprint)throw new ApiError(409,"database_mismatch","工作只能在相同資料庫指紋續跑。");
        if(j.LeaseToken!=c.LeaseToken || j.LeaseExpires<=time.GetUtcNow() || j.LeaseExpires is null)throw new ApiError(409,"lease_lost","工作租約不存在或已到期。");
    }
    public JobRecord Lease(string id,LeaseRequest r)=>store.Update<JobRecord>("jobs",id,j=>
    {
        Contract.Check(r);if(j.DatabaseFingerprint!=r.DatabaseFingerprint)throw new ApiError(409,"database_mismatch","資料庫指紋不相符。");
        if(j.Status is "completed" or "cancelled")throw new ApiError(409,"invalid_state","此工作已結束。");
        if(j.LeaseExpires>time.GetUtcNow())throw new ApiError(409,"lease_conflict","此工作已有有效租約。");
        var recovered=j.Items.Select(x=>x with {Tokens=checked(x.Tokens+x.ReservedTokens),ReservedTokens=0,ReservationLeaseToken=null}).ToArray();
        if(recovered.Sum(x=>(decimal)x.Tokens)>=j.Budget.Tokens)return j with {Items=recovered,Status="budget-exhausted",CancelRequested=true,LeaseToken=null,LeaseExpires=null,WorkerId=null};
        return j with {Items=recovered,WorkerId=r.WorkerId,LeaseToken=Contract.Id(),LeaseExpires=time.GetUtcNow().AddSeconds(r.LeaseSeconds)};
    });
    public JobRecord Command(string id,string action,JobCommand r)=>store.Update<JobRecord>("jobs",id,j=>
    {
        Active(j,r); if(j.StartedAt is not null && (time.GetUtcNow()-j.StartedAt.Value).TotalSeconds>j.Budget.TimeSeconds && action!="release")throw new ApiError(409,"budget_exhausted","整批時間預算已用盡。");
        if(action=="renew")return j with {LeaseExpires=time.GetUtcNow().AddSeconds(60)};
        if(action=="release")return j with {WorkerId=null,LeaseToken=null,LeaseExpires=null};
        if(action=="start") {if(j.Status is not ("pending" or "running"))throw new ApiError(409,"invalid_state","無法啟動此工作。");if(j.CancelRequested)throw new ApiError(409,"cancel_requested","工作已要求取消。");return j with {Status="running",StartedAt=j.StartedAt??time.GetUtcNow()};}
        if(action=="complete") {if(j.Status!="running" || j.Items.Any(x=>x.Status is "pending" or "running"))throw new ApiError(409,"incomplete_items","仍有未完成項目。");return j with {Status=j.CancelRequested?"cancelled":"completed",LeaseExpires=null,LeaseToken=null,WorkerId=null};}
        throw new ApiError(400,"unsupported","不支援此動作。");
    });
    public JobRecord Cancel(string id)=>store.Update<JobRecord>("jobs",id,j=>j.Status=="completed"?throw new ApiError(409,"invalid_state","工作已完成。"):j with {CancelRequested=true,Status="cancelled",LeaseToken=null,LeaseExpires=null,WorkerId=null});
    public JobRecord Retry(string id)=>store.Update<JobRecord>("jobs",id,j=>
    {
        if(j.LeaseExpires>time.GetUtcNow())throw new ApiError(409,"lease_conflict","請先釋放租約。"); if(j.Status=="budget-exhausted" || (j.StartedAt is not null && (time.GetUtcNow()-j.StartedAt.Value).TotalSeconds>j.Budget.TimeSeconds))throw new ApiError(409,"budget_exhausted","整批預算已用盡，請建立新工作。");
        var items=j.Items.Select(x=>x.Status=="failed" && x.Attempts<=j.MaxRetries?x with {Status="pending"}:x).ToArray();
        if(!items.Any(x=>x.Status=="pending"))throw new ApiError(409,"retry_exhausted","無可重試項目或已達上限。");
        return j with {Items=items,Status="pending",CancelRequested=false,LeaseToken=null,LeaseExpires=null,WorkerId=null};
    });
    public JobRecord Replay(string id,bool demo) {var j=Get(id);return Create(new(j.DatabaseFingerprint,j.TopN,j.Items.Select(x=>x.ObjectId).ToArray(),j.Budget,j.MaxRetries),demo);}
    public int ReserveAi(string id,JobCommand command,int objectId,long inputUpperBound)
    {
        int outputLimit=0;
        store.Update<JobRecord>("jobs",id,j=>
        {
            Active(j,command);
            if(j.Status!="running" || j.CancelRequested || (time.GetUtcNow()-j.StartedAt)?.TotalSeconds>j.Budget.TimeSeconds)throw new ApiError(409,"invalid_state","工作未執行或時間預算已用盡。");
            var item=j.Items.SingleOrDefault(x=>x.ObjectId==objectId)??throw new ApiError(404,"item_not_found","工作內沒有此物件。");
            if(item.Status is not ("pending" or "running") || item.ReservedTokens>0)throw new ApiError(409,"reservation_conflict","此項目已結束或已有 AI 呼叫執行中。");
            var remaining=j.Budget.Tokens-j.Items.Sum(x=>x.Tokens+x.ReservedTokens);
            if(remaining<=inputUpperBound)throw new ApiError(409,"token_budget","剩餘 token 預算不足以安全預留本次輸入。");
            outputLimit=(int)Math.Min(8000,remaining-inputUpperBound);
            return j with {Items=j.Items.Select(x=>x.ObjectId==objectId?x with {ReservedTokens=inputUpperBound+outputLimit,ReservationLeaseToken=command.LeaseToken}:x).ToArray()};
        });
        return outputLimit;
    }
    public void SettleAi(string id,int objectId,string leaseToken,long? actualTokens)
    {
        store.Update<JobRecord>("jobs",id,j=>
        {
            var item=j.Items.Single(x=>x.ObjectId==objectId);
            // Unknown usage after failure is conservatively charged at the reservation.
            if(item.ReservationLeaseToken!=leaseToken)return j; var charged=actualTokens??item.ReservedTokens;
            var items=j.Items.Select(x=>x.ObjectId==objectId?x with {Tokens=checked(x.Tokens+charged),ReservedTokens=0,ReservationLeaseToken=null}:x).ToArray();
            bool exhausted=items.Sum(x=>(decimal)x.Tokens)>=j.Budget.Tokens;
            return j with {Items=items,CancelRequested=j.CancelRequested||exhausted,Status=exhausted && j.Status=="running"?"budget-exhausted":j.Status};
        });
    }
    public JobRecord Checkpoint(string id,int objectId,CheckpointRequest r)=>store.Update<JobRecord>("jobs",id,j=>
    {
        Contract.Check(r);Active(j,new(r.LeaseToken,r.DatabaseFingerprint));Contract.Choice(r.Status,"running","completed","failed","skipped");
        if(j.Status!="running" || j.CancelRequested)throw new ApiError(409,"invalid_state","工作未執行或已取消。");
        var old=j.Items.SingleOrDefault(x=>x.ObjectId==objectId)??throw new ApiError(404,"item_not_found","工作內沒有此物件。");
        if(r.Sequence==old.Sequence && old.Status==r.Status && old.ElapsedMs==r.ElapsedMs && old.CpuMs==r.CpuMs && old.LogicalReads==r.LogicalReads && old.Tokens==r.Tokens)return j;
        if(r.Sequence<=old.Sequence || old.Status is "completed" or "failed" or "skipped" || r.ElapsedMs<old.ElapsedMs || r.CpuMs<old.CpuMs || r.LogicalReads<old.LogicalReads || r.Tokens<old.Tokens)throw new ApiError(409,"checkpoint_conflict","檢查點順序或累計用量不正確。");
        if(old.ReservedTokens>0)throw new ApiError(409,"reservation_conflict","AI 呼叫仍執行中，尚不可提交檢查點。"); var attempts=old.Attempts+(old.Status=="pending"?1:0);if(attempts>j.MaxRetries+1)throw new ApiError(409,"retry_exhausted","此項目已達重試上限。");
        var items=j.Items.Select(x=>x.ObjectId==objectId?new JobItem(objectId,r.Status,attempts,r.Sequence,r.ElapsedMs,r.CpuMs,r.LogicalReads,r.Tokens):x).ToArray();
        var exhausted=items.Sum(x=>(decimal)x.ElapsedMs)>j.Budget.TimeSeconds*1000m || items.Sum(x=>(decimal)x.CpuMs)>j.Budget.CpuMs || items.Sum(x=>(decimal)x.LogicalReads)>j.Budget.LogicalReads || items.Sum(x=>(decimal)x.Tokens)>j.Budget.Tokens || (time.GetUtcNow()-j.StartedAt)?.TotalSeconds>j.Budget.TimeSeconds;
        return j with {Items=items,CancelRequested=exhausted,Status=exhausted?"budget-exhausted":j.Status};
    });
}
