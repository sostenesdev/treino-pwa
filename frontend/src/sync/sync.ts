import { api, token } from '../shared/api';
import type { Account, Bootstrap, Operation } from '../shared/types';
import { acknowledge, markError, markSending, pending, saveBootstrap } from '../offline/store';
export function deviceId() { let id=localStorage.getItem('treinos-device-id'); if(!id){id=crypto.randomUUID();localStorage.setItem('treinos-device-id',id);} return id; }
export function networkType(): string { return (navigator as Navigator & { connection?: { type?: string } }).connection?.type || 'unknown'; }
export async function prepare(account:Account) { const data=await api<Bootstrap>('/sync/bootstrap'); await saveBootstrap(account.id,data); await navigator.storage?.persist?.(); return data; }
export async function synchronize(account:Account, manual=false): Promise<string> {
  if (!manual && networkType()!=='wifi') return 'Aguardando Wi-Fi ou confirmação manual.';
  try { await api('/health/live'); const me=await api<Account>('/auth/me'); if(me.id!==account.id) return 'Entre novamente com a mesma conta.'; await token(); } catch { return 'API indisponível ou sessão expirada.'; }
  let done=0; const operations=await pending(account.id);
  for(const original of operations) {
    const latest=await pending(account.id); const op=latest.find(x=>x.operationId===original.operationId); if(!op) continue; if(latest.some(x=>x.entityId===op.entityId&&x.createdAt<op.createdAt)) continue;
    if(op.state==='conflict'||op.state==='failed') continue;
    await markSending(account.id,op);
    try { const {state,attempted,error,owner,createdAt,...payload}=op; void state; void attempted; void error; void owner; void createdAt; const ack=await api<{version:number}>('/sync/operations',{method:'POST',body:JSON.stringify(payload)}); await acknowledge(account.id,op,ack.version); done++; }
    catch(e) { const err=e as Error & {status?:number}; await markError(account.id,op,err.status===401?'auth_required':err.status===409?'conflict':err.status===400?'failed':'pending',err.message); if(err.status===401) break; }
  }
  try { const fresh=await api<Bootstrap>('/sync/bootstrap'); await saveBootstrap(account.id,fresh); } catch { /* upload já confirmado permanece salvo */ }
  return `${done} operação(ões) sincronizada(s).`;
}
