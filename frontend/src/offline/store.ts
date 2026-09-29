import { openDB } from 'idb';
import type { Account, Bootstrap, Operation, Session } from '../shared/types';
const db = openDB('treinos-v1', 1, { upgrade(db) { db.createObjectStore('accounts'); db.createObjectStore('snapshots'); db.createObjectStore('sessions'); db.createObjectStore('outbox'); db.createObjectStore('meta'); } });
const key = (user: string, id: string) => `${user}:${id}`;
const opKey = (user:string, op:Operation) => `${user}:${op.entityId}:${op.operationId}`;
export async function saveAccount(account: Account) { await (await db).put('accounts', account, account.id); sessionStorage.setItem('activeUser', account.id); }
export async function activeAccount(): Promise<Account|null> { const id = sessionStorage.getItem('activeUser'); return id ? (await (await db).get('accounts', id)) || null : null; }
export async function lockAccount() { sessionStorage.removeItem('activeUser'); }
export async function saveBootstrap(user: string, data: Bootstrap) {
  const d = await db; const tx = d.transaction(['snapshots','sessions','meta','outbox'], 'readwrite');
  await tx.objectStore('snapshots').put(data, user); await tx.objectStore('meta').put({ cursor: data.revision, preparedAt: new Date().toISOString() }, user);
  const allOps:Operation[]=await tx.objectStore('outbox').getAll(); const pendingIds=new Set(allOps.filter(o=>o.owner===user).map(o=>o.entityId));
  for (const session of data.sessions) if(!pendingIds.has(session.id)) await tx.objectStore('sessions').put(session,key(user,session.id));
  await tx.done;
}
export async function snapshot(user: string): Promise<Bootstrap|null> { return (await (await db).get('snapshots', user)) || null; }
export async function meta(user: string): Promise<{cursor:number;preparedAt:string}|null> { return (await (await db).get('meta', user)) || null; }
export async function sessions(user: string): Promise<Session[]> { const d=await db; const keys=await d.getAllKeys('sessions'); const values=await Promise.all(keys.filter(k=>String(k).startsWith(user+':')).map(k=>d.get('sessions',k))); return values.filter((x):x is Session=>!!x&&!x.deleted).sort((a,b)=>b.performedOn.localeCompare(a.performedOn)); }
export async function pending(user: string): Promise<Operation[]> { const d=await db; const all:Operation[]=await d.getAll('outbox'); return all.filter(o=>o.owner===user).sort((a,b)=>a.createdAt-b.createdAt); }
export async function saveSession(user: string, session: Session, deviceId: string) {
  const d=await db; const k=key(user,session.id); const tx=d.transaction(['sessions','outbox'],'readwrite');
  const all:Operation[]=await tx.objectStore('outbox').getAll(); const ops=all.filter(o=>o.owner===user&&o.entityId===session.id).sort((a,b)=>a.createdAt-b.createdAt); const last=ops.at(-1);
  if(session.deleted&&session.rowVersion===0&&ops.length===1&&!ops[0].attempted){await tx.objectStore('sessions').delete(k);await tx.objectStore('outbox').delete(opKey(user,ops[0]));await tx.done;return;}
  const kind=session.deleted?'delete':session.rowVersion===0&&!ops.length?'create':'replace';
  const operation:Operation={operationId:crypto.randomUUID(),deviceId,schemaVersion:1,entityId:session.id,kind,baseVersion:session.rowVersion,snapshot:session.deleted?null:session,state:'pending',attempted:false,owner:user,createdAt:Math.max(Date.now(),(last?.createdAt??0)+1)};
  if(last&&!last.attempted){await tx.objectStore('outbox').delete(opKey(user,last));operation.kind=last.kind==='create'&&!session.deleted?'create':kind;operation.baseVersion=last.baseVersion;operation.createdAt=last.createdAt;}
  await tx.objectStore('sessions').put(session,k);await tx.objectStore('outbox').put(operation,opKey(user,operation));await tx.done;
}
export async function markSending(user:string, op:Operation) { const d=await db; const k=opKey(user,op); const tx=d.transaction('outbox','readwrite'); const current:Operation|undefined=await tx.store.get(k); if(current)await tx.store.put({...current,state:'sending',attempted:true},k); await tx.done; }
export async function markError(user:string, op:Operation, state:Operation['state'], error:string) { const d=await db; const k=opKey(user,op); const tx=d.transaction('outbox','readwrite'); const current:Operation|undefined=await tx.store.get(k); if(current)await tx.store.put({...current,state,error},k); await tx.done; }
export async function acknowledge(user:string, op:Operation, version:number) { const d=await db; const tx=d.transaction(['outbox','sessions'],'readwrite'); await tx.objectStore('outbox').delete(opKey(user,op));const all:Operation[]=await tx.objectStore('outbox').getAll();const next=all.filter(x=>x.owner===user&&x.entityId===op.entityId).sort((a,b)=>a.createdAt-b.createdAt)[0];const k=key(user,op.entityId);const session:Session|undefined=await tx.objectStore('sessions').get(k);if(next){await tx.objectStore('outbox').put({...next,baseVersion:version,kind:next.kind==='create'?'replace':next.kind},opKey(user,next));if(session)await tx.objectStore('sessions').put({...session,rowVersion:version},k);}else if(session)await tx.objectStore('sessions').put({...session,rowVersion:version},k);await tx.done; }
