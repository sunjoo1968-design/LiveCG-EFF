// Actual Chromium/CEF-family regression checks without a server or external JS dependencies.
import { spawn, spawnSync } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

const root = path.resolve(process.argv[2] ?? ".");
const folder = path.join(root, "artifacts/v010-core"), results = [];
const chrome = process.env.LIVECG_TEST_CHROME ?? "C:/Program Files/Google/Chrome/Application/chrome.exe";
const profile = path.join(root, "artifacts/broadcast-browser-profile");
const child = spawn(chrome, ["--headless=new", "--remote-debugging-pipe", `--user-data-dir=${profile}`, "--no-first-run", "--no-default-browser-check", "--disable-background-networking", "--hide-scrollbars", "about:blank"], { windowsHide: true, stdio: ["ignore", "ignore", "pipe", "pipe", "pipe"] });
let sequence = 0, pending = new Map(), buffer = Buffer.alloc(0), stderr = "";
child.stderr.on("data", b => { stderr += b; });
child.stdio[4].on("data", bytes => {
    buffer = Buffer.concat([buffer, bytes]); let end;
    while ((end = buffer.indexOf(0)) >= 0) {
        const chunk = buffer.subarray(0, end).toString(); buffer = buffer.subarray(end + 1); if (!chunk) continue;
        const msg = JSON.parse(chunk), p = pending.get(msg.id); if (!p) continue; pending.delete(msg.id); clearTimeout(p.timer);
        msg.error ? p.reject(new Error(JSON.stringify(msg.error))) : p.resolve(msg.result);
    }
});
function send(method, params = {}, sessionId) {
    return new Promise((resolve, reject) => { const id = ++sequence, timer = setTimeout(() => { pending.delete(id); reject(new Error("CDP timeout " + method + " " + stderr.slice(-1000))); }, 20000); pending.set(id, { resolve, reject, timer }); child.stdio[3].write(JSON.stringify({ id, method, params, ...(sessionId ? { sessionId } : {}) }) + "\0"); });
}
function check(ok, name) { results.push({ ok: !!ok, name }); console.log((ok ? "PASS " : "FAIL ") + name); }
const sleep = ms => new Promise(r => setTimeout(r, ms));
let session;
async function evaluate(expression) {
    const r = await send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true }, session);
    if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails)); return r.result.value;
}
const fixture=JSON.parse(await fs.readFile(path.join(folder,"broadcast/browser-test.json"),"utf8"));
const pointer=path.join(path.dirname(fixture.entry),"pointer.js");
const update=async(revision,immediate)=>{
 const temporary=pointer+".test.tmp";
 await fs.writeFile(temporary,"window.LiveCGUpdate("+JSON.stringify({revision:revision.Revision,immediate,binding:{Active:revision,Previous:revision.Revision===fixture.first.Revision?fixture.second:fixture.first,Immediate:immediate}})+");");
 await fs.rename(temporary,pointer);
};
const waitFor=async(predicate)=>{
 let state;
 for(let i=0;i<200;i++){
  state=await evaluate("window.LiveCG?.status()").catch(()=>null);
  if(predicate(state))return state;
  await sleep(50);
 }
 throw new Error("Timed out: "+JSON.stringify(state));
};
try{
 await update(fixture.first,true);
 const target=await send("Target.createTarget",{url:"about:blank"});
 session=(await send("Target.attachToTarget",{targetId:target.targetId,flatten:true})).sessionId;
 await send("Page.enable",{},session);await send("Runtime.enable",{},session);
 await send("Emulation.setDeviceMetricsOverride",{width:160,height:100,deviceScaleFactor:1,mobile:false},session);
 await send("Page.navigate",{url:pathToFileURL(fixture.entry).href},session);
 await waitFor(s=>s?.loaded&&s.revision===fixture.first.Revision);
 check(true,"fixed local entry loads the first immutable package");
 await evaluate("window.dispatchEvent(new CustomEvent('obsSourceActiveChanged',{detail:{active:false}}))");
 await sleep(100); check((await evaluate("LiveCG.status()")).waitingForProgram,"wrapper forwards OBS preview deactivation");
 await evaluate("window.dispatchEvent(new CustomEvent('obsSourceActiveChanged',{detail:{active:true}}))");
 await sleep(100); check(!(await evaluate("LiveCG.status()")).waitingForProgram,"wrapper restarts on program activation");
 await evaluate("LiveCG.restart()");
 await update(fixture.second,false);
 await waitFor(s=>s?.pending===fixture.second.Revision);
 check((await evaluate("LiveCG.status()")).revision===fixture.first.Revision,"deferred update prepares while the current package remains visible");
 await waitFor(s=>s?.revision===fixture.second.Revision);
 check(true,"loop boundary activates the prepared revision without source reload");
 await sleep(100);
 const shot=await send("Page.captureScreenshot",{format:"png"},session);
 const screenshot=path.join(path.dirname(fixture.entry),"actual-update.png");
 await fs.writeFile(screenshot,Buffer.from(shot.data,"base64"));
 const pixels=spawnSync(path.join(root,".tools/dotnet/dotnet.exe"),[path.join(root,"tests/LiveCG.Verify/bin/Debug/net10.0/LiveCG.Verify.dll"),root,"--broadcast-pixel",screenshot],{encoding:"utf8",windowsHide:true});
 check(pixels.status===0,"replacement renders the new blue source pixels"); await update(fixture.first,true);
 await waitFor(s=>s?.revision===fixture.first.Revision);
 check(true,"immediate rollback reloads the original package at the same OBS path");
 await evaluate("window.dispatchEvent(new CustomEvent('obsSourceActiveChanged',{detail:{active:false}}))");
 await sleep(100);
 await update(fixture.second,true);
 await waitFor(s=>s?.revision===fixture.second.Revision&&s.waitingForProgram);
 check(true,"replacement while in preview preserves program waiting state");
 await evaluate("window.dispatchEvent(new CustomEvent('obsSourceActiveChanged',{detail:{active:true}}))");
 const resumed=await waitFor(s=>s?.programActive===true&&!s.waitingForProgram);
 check(resumed.time<.6,"updated preview replays from entrance on program activation");
 await update({Revision:"00000000000000000000000000000000"},true);
 await sleep(1800);
 check((await evaluate("LiveCG.status()")).revision===fixture.second.Revision,"missing revision keeps the previous package visible");
 await update(fixture.first,true);
 console.log("RESULT "+results.filter(r=>r.ok).length+"/"+results.length+" passed");
 if(results.some(r=>!r.ok))process.exitCode=1;
}finally{await send("Browser.close").catch(()=>{});child.kill();}
