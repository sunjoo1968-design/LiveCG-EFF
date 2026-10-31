// Isolated, portable OBS integration test. Never uses the user's profile or starts a stream/recording.
import fs from 'node:fs/promises';import path from 'node:path';import {spawn} from 'node:child_process';import crypto from 'node:crypto';
const root=path.resolve(process.argv[2]??'.'), portable=path.join(root,'artifacts/v010-obs-portable'),config=path.join(portable,'config/obs-studio'),password=crypto.randomBytes(24).toString('hex'),port=4469,results=[];
const sleep=ms=>new Promise(r=>setTimeout(r,ms));const check=(ok,name)=>{results.push({ok:!!ok,name});console.log((ok?'PASS ':'FAIL ')+name);};
try{await fs.access(path.join(portable,'bin/64bit/obs64.exe'));}catch{
    const installed=process.env.LIVECG_OBS_INSTALL??'C:/Program Files/obs-studio';
    await fs.access(path.join(installed,'bin/64bit/obs64.exe')).catch(()=>{throw new Error('OBS 설치 경로가 없습니다. LIVECG_OBS_INSTALL 환경변수로 OBS33 설치 폴더를 지정하세요.');});
    await fs.cp(installed,portable,{recursive:true});await fs.writeFile(path.join(portable,'portable_mode.txt'),'');
}
await fs.mkdir(path.join(config,'plugin_config/obs-websocket'),{recursive:true});
await fs.writeFile(path.join(config,'plugin_config/obs-websocket/config.json'),JSON.stringify({server_enabled:true,server_port:port,auth_required:true,server_password:password,alerts_enabled:false,first_load:false}));
await fs.writeFile(path.join(config,'global.ini'),'[General]\nFirstRun=false\nLastVersion=553648128\nEnableAutoUpdates=false\n\n[Basic]\nProfile=LiveCGTest\nProfileDir=LiveCGTest\nSceneCollection=LiveCGTest\nSceneCollectionFile=LiveCGTest\n\n[BasicWindow]\nStudioMode=true\n');
await fs.mkdir(path.join(config,'basic/profiles/LiveCGTest'),{recursive:true});await fs.writeFile(path.join(config,'basic/profiles/LiveCGTest/basic.ini'),'[General]\nName=LiveCGTest\n[Video]\nBaseCX=160\nBaseCY=100\nOutputCX=160\nOutputCY=100\nFPSType=0\nFPSCommon=30\n');
const folder=path.join(root,'artifacts/v010-core/한글 로컬 & 패키지.obs');
const html=await fs.readFile(path.join(folder,'index.html'),'utf8');
const probe=html.replace('</body>','<script>setInterval(()=>{if(window.LiveCG)console.error("[OBS-PROBE] "+JSON.stringify({...LiveCG.status(),probe:"transition"}))},100);window.addEventListener("ProbeSeek",e=>LiveCG.renderAt(e.detail.time));</script></body>');await fs.writeFile(path.join(folder,'obs-probe.html'),probe);
const child=spawn(path.join(portable,'bin/64bit/obs64.exe'),['--portable','--multi','--disable-updater','--disable-missing-files-check','--disable-shutdown-check','--minimize-to-tray'],{cwd:path.join(portable,'bin/64bit'),windowsHide:true,stdio:'ignore'});
let ws,seq=0;const pending=new Map();let logFile;const renderCaptures=[];
function request(requestType,requestData={}){return new Promise((resolve,reject)=>{const id=String(++seq),timer=setTimeout(()=>{pending.delete(id);reject(new Error('OBS timeout '+requestType));},7000);pending.set(id,{resolve,reject,timer});ws.send(JSON.stringify({op:6,d:{requestType,requestId:id,requestData}}));});}
async function connect(){
    for(let i=0;i<160;i++){
        try{ws=await new Promise((resolve,reject)=>{const socket=new WebSocket(`ws://127.0.0.1:${port}`);socket.addEventListener('error',()=>reject(new Error('connecting')),{once:true});socket.addEventListener('open',()=>resolve(socket),{once:true});});break;}catch{await sleep(100);}
    }
    if(!ws)throw new Error('Isolated OBS WebSocket did not start');
    await new Promise((resolve,reject)=>{ws.addEventListener('message',event=>{const m=JSON.parse(event.data);if(m.op===0){const a=m.d.authentication;let authentication;if(a){const secret=crypto.createHash('sha256').update(password+a.salt).digest('base64');authentication=crypto.createHash('sha256').update(secret+a.challenge).digest('base64');}ws.send(JSON.stringify({op:1,d:{rpcVersion:1,eventSubscriptions:0,...(authentication?{authentication}:{})}}));}else if(m.op===2)resolve();else if(m.op===7){const p=pending.get(m.d.requestId);if(p){clearTimeout(p.timer);pending.delete(m.d.requestId);m.d.requestStatus.result?p.resolve(m.d.responseData):p.reject(new Error(m.d.requestType+': '+JSON.stringify(m.d.requestStatus)));}}});ws.addEventListener('close',()=>reject(new Error('OBS connection closed')));});
}
async function samples(){if(!logFile){const names=await fs.readdir(path.join(config,'logs'));logFile=path.join(config,'logs',names.filter(f=>f.endsWith('.txt')).sort().at(-1));}const text=await fs.readFile(logFile,'utf8');return [...text.matchAll(/\[OBS-PROBE\]\s*(\{[^\n]+\})/g)].map(m=>{try{return JSON.parse(m[1]);}catch{return null;}}).filter(Boolean);}
async function waitProbe(predicate){for(let i=0;i<80;i++){const rows=await samples().catch(()=>[]);if(rows.some(predicate))return rows;await sleep(100);}return samples();}
try{
    await connect();let version;for(let i=0;i<150;i++){try{version=await request('GetVersion');break;}catch(error){if(!error.message.includes('207'))throw error;await sleep(100);}}if(!version)throw new Error('OBS frontend initialization did not finish');check(version.obsVersion.startsWith('33.'),'isolated actual OBS 33 started with test-only profile');
    await request('CreateSceneCollection',{sceneCollectionName:'LiveCGProbe-'+Date.now()});
    for(const sceneName of ['LiveCG Blank','LiveCG Browser'])await request('CreateScene',{sceneName});
    const mode=await request('GetStudioModeEnabled');if(!mode.studioModeEnabled)await request('SetStudioModeEnabled',{studioModeEnabled:true});await request('SetCurrentProgramScene',{sceneName:'LiveCG Blank'});
    const inputName='LiveCG Program Probe';try{await request('RemoveInput',{inputName});}catch{}
    await request('CreateInput',{sceneName:'LiveCG Browser',inputName,inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(folder,'obs-probe.html'),width:160,height:100,fps_custom:true,fps:30,shutdown:false,restart_when_active:false},sceneItemEnabled:true});
    await request('SetCurrentPreviewScene',{sceneName:'LiveCG Browser'});
    let rows=await waitProbe(s=>s.loaded);await sleep(700);rows=await samples();const last=rows.at(-1);const sourceState=await request('GetSourceActive',{sourceName:inputName});
    check(sourceState.videoShowing&&!sourceState.videoActive&&last?.loaded,'actual OBS preview is showing but not program-active before transition');
    const transitions=await request('GetSceneTransitionList');const cut=transitions.transitions.find(t=>t.transitionKind==='cut_transition');if(cut)await request('SetCurrentSceneTransition',{transitionName:cut.transitionName});
    const before=rows.length;await request('TriggerStudioModeTransition');rows=await waitProbe((s,i)=>s.programActive===true&&s.time<.3);await sleep(350);rows=await samples();const shown=rows.slice(before).filter(s=>s.programActive===true);
    check(shown.length>0&&shown[0].time<.3&&!shown[0].waitingForProgram,'actual preview-to-program transition starts entrance from frame zero');
    await sleep(500);rows=await samples();check(rows.at(-1)?.time>.4,'actual program playback advances after the first entrance frame');
    await request('SetCurrentPreviewScene',{sceneName:'LiveCG Blank'});await request('TriggerStudioModeTransition');await sleep(400);rows=await samples();check(rows.at(-1)?.waitingForProgram&&rows.at(-1)?.time<.01,'returning source to preview resets the waiting frame');
    await request('SetCurrentPreviewScene',{sceneName:'LiveCG Browser'});await sleep(700);const start=(await samples()).length;await request('TriggerStudioModeTransition');await sleep(500);rows=(await samples()).slice(start).filter(s=>s.programActive===true);check(rows.length>0&&rows[0].time<.3,'second actual program transition replays entrance even after a long preview');
    const reloadStart=(await samples()).length;await request('PressInputPropertiesButton',{inputName,propertyName:'refreshnocache'});await sleep(1000);rows=(await samples()).slice(reloadStart);check(rows.some(s=>s.loaded&&!s.waitingForProgram&&s.time>.1),'actual source refresh while program-active also starts playback');
    // Studio Mode may output a private scene copy; new inputs must be tested in the actual program scene.
    await request('SetStudioModeEnabled',{studioModeEnabled:false});await request('SetCurrentProgramScene',{sceneName:'LiveCG Browser'});
    await fs.writeFile(path.join(folder,'obs-startup-probe.html'),probe.replace('probe:"transition"','probe:"startup"'));
    await request('CreateInput',{sceneName:'LiveCG Browser',inputName:'LiveCG Startup Probe',inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(folder,'obs-startup-probe.html'),width:160,height:100,shutdown:false,restart_when_active:false},sceneItemEnabled:true});await sleep(1200);rows=(await samples()).filter(s=>s.probe==='startup');const startupState=await request('GetSourceActive',{sourceName:'LiveCG Startup Probe'});check(startupState.videoActive&&rows.some(s=>s.loaded&&!s.waitingForProgram&&s.time>.1),'new browser source added directly to actual program starts without needing a scene change');
    const colorFolder=path.join(root,'artifacts/v010-core/broadcast-polish.obs'),colorHtml=await fs.readFile(path.join(colorFolder,'index.html'),'utf8');
    for(const time of [1.4,3.5]){
        const filename=`obs-render-${time}.html`;await fs.writeFile(path.join(colorFolder,filename),colorHtml.replace('</body>',`<script>let reported=false;setInterval(()=>{if(window.LiveCG?.status().loaded){LiveCG.renderAt(${time});if(!reported){reported=true;console.error("[OBS-RENDER-${time}] "+JSON.stringify([...document.getElementById("livecg").getContext("2d").getImageData(80,50,1,1).data]))}}},50)</script></body>`));
        const renderName='LiveCG Render '+time;await request('CreateInput',{sceneName:'LiveCG Browser',inputName:renderName,inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(colorFolder,filename),width:160,height:100,fps_custom:true,fps:30,shutdown:false,restart_when_active:false},sceneItemEnabled:true});
        let ready=false;for(let i=0;i<100;i++){const log=await fs.readFile(logFile,'utf8');if(log.includes(`[OBS-RENDER-${time}]`)){ready=true;break;}await sleep(100);}check(ready,'actual CEF canvas is ready before screenshot at '+time+'s');await sleep(200);
        const renderState=await request('GetSourceActive',{sourceName:renderName});check(renderState.videoActive&&renderState.videoShowing,'screenshot source belongs to actual program at '+time+'s');
        const shot=await request('GetSourceScreenshot',{sourceName:renderName,imageFormat:'png',imageWidth:160,imageHeight:100});const actual=path.join(colorFolder,`actual-obs-${time.toFixed(2)}.png`);await fs.writeFile(actual,Buffer.from(shot.imageData.split(',')[1],'base64'));renderCaptures.push({package:'Actual OBS 33',premultiplied:true,time,expected:path.join(colorFolder,'expected',time.toFixed(2)+'.png'),actual});check(shot.imageData.startsWith('data:image/png;base64,'),'actual OBS CEF renders colored sweep/crop at '+time+'s');
    }
    for(const [name,time] of [['soft-outside',.3],['soft-group',.25],['content-only',.7],['manual-separation',.6],['inactive-manual-group',.6]]){
        const dir=path.join(root,'artifacts/v010-core',name+'.obs'),html=await fs.readFile(path.join(dir,'index.html'),'utf8'),marker='[SOFT-'+name+']';
        await fs.writeFile(path.join(dir,'obs-soft.html'),html.replace('</body>',`<script>let ready=false;setInterval(()=>{if(window.LiveCG?.status().loaded){LiveCG.renderAt(${time});if(!ready){ready=true;console.error("${marker}")}}},50)</script></body>`));
        const inputName='Soft '+name;await request('CreateInput',{sceneName:'LiveCG Browser',inputName,inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(dir,'obs-soft.html'),width:160,height:120,fps_custom:true,fps:30,shutdown:false,restart_when_active:false},sceneItemEnabled:true});
        let ready=false;for(let i=0;i<100;i++){if((await fs.readFile(logFile,'utf8')).includes(marker)){ready=true;break;}await sleep(100);}check(ready,'actual OBS ready for '+name);await sleep(200);
        const shot=await request('GetSourceScreenshot',{sourceName:inputName,imageFormat:'png',imageWidth:160,imageHeight:120}),actual=path.join(dir,'actual-obs-soft.png');await fs.writeFile(actual,Buffer.from(shot.imageData.split(',')[1],'base64'));renderCaptures.push({package:'Actual OBS '+name,premultiplied:true,time,expected:path.join(dir,'expected',String(time)+'.png'),actual});check(shot.imageData.startsWith('data:image/png;base64,'),'actual OBS soft/content/group capture '+name);
    }
}catch(error){check(false,error.stack);}
finally{if(ws)ws.close();await fs.writeFile(path.join(root,'artifacts/v010-obs-results.json'),JSON.stringify({passed:results.filter(r=>r.ok).length,total:results.length,log:logFile,results},null,2));await fs.writeFile(path.join(root,'artifacts/v010-obs-render-captures.json'),JSON.stringify(renderCaptures,null,2));child.kill();}
console.log(`RESULT ${results.filter(r=>r.ok).length}/${results.length} passed`);process.exitCode=results.some(r=>!r.ok)?1:0;
