// Isolated, portable OBS integration test. Never uses the user's profile or starts a stream/recording.
import fs from 'node:fs/promises';import path from 'node:path';import {spawn,spawnSync} from 'node:child_process';import crypto from 'node:crypto';
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
const linkedMode=process.argv.includes("--linked-output");
const digitPreview=process.argv.includes("--digit-preview");
const updateMode=linkedMode||process.argv.includes("--broadcast-updates");
let updateFixture=updateMode&&!linkedMode?JSON.parse(await fs.readFile(path.join(root,"artifacts/v010-core/broadcast/browser-test.json"),"utf8")):null;
if(linkedMode){
 const linked=JSON.parse(await fs.readFile(path.join(root,"artifacts/v010-core/linked-browser-test.json"),"utf8"));
 const decode=text=>JSON.parse(text.slice("window.LiveCGUpdate(".length,-2));
 updateFixture={entry:linked.entry,first:{Revision:decode(linked.firstPointer).revision},second:{Revision:decode(linked.secondPointer).revision}};
 await fs.writeFile(path.join(path.dirname(linked.entry),"pointer.js"),linked.firstPointer);
}
const folder=digitPreview?path.join(root,'artifacts/digit-preview-test.obs'):updateMode?path.dirname(updateFixture.entry):path.join(root,'artifacts/v010-core/한글 로컬 & 패키지.obs');
if(digitPreview){
    await fs.cp(path.join(root,'artifacts/v010-core/redraw-price.obs'),folder,{recursive:true});
    await fs.copyFile(path.join(root,'src/LiveCG.Core/Browser/player.js'),path.join(folder,'player.js'));
    await fs.appendFile(path.join(folder,'data.js'),'\nLIVECG_DATA.plan.endFrame=42;\n');
}
const html=await fs.readFile(path.join(folder,updateMode&&!linkedMode?'entry.html':'index.html'),'utf8');
const priceProbe=digitPreview?`const original=new Image();original.src=LIVECG_DATA.source;let pixels;
function originalPrice(){if(!original.complete||!original.naturalWidth)return null;const canvas=document.getElementById("livecg");
if(!pixels){const ref=document.createElement("canvas");ref.width=canvas.width;ref.height=canvas.height;const ctx=ref.getContext("2d");ctx.drawImage(original,0,0);pixels=ctx.getImageData(0,0,ref.width,ref.height).data;}
const live=canvas.getContext("2d").getImageData(0,0,canvas.width,canvas.height).data;
return pixels.every((value,index)=>value===live[index]);}`:'function originalPrice(){return null;}';
const probe=html.replace('</body>',`<script>${priceProbe}
setInterval(()=>{if(window.LiveCG)console.error("[OBS-PROBE] "+JSON.stringify({...LiveCG.status(),probe:"transition",originalPrice:originalPrice()}))},100);
window.addEventListener("ProbeSeek",e=>LiveCG.renderAt(e.detail.time));</script></body>`);await fs.writeFile(path.join(folder,'obs-probe.html'),probe);if(linkedMode)await fs.writeFile(path.join(folder,'index.html'),probe);
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
async function verifyBroadcastUpdates(){
 const fixture=updateFixture;
 const collectionName="LiveCGUpdates-"+Date.now();
 await request("CreateSceneCollection",{sceneCollectionName:collectionName});
 for(const sceneName of ["Updates Blank","Updates Browser"])await request("CreateScene",{sceneName});
 if(!(await request("GetStudioModeEnabled")).studioModeEnabled)await request("SetStudioModeEnabled",{studioModeEnabled:true});
 await request("SetCurrentProgramScene",{sceneName:"Updates Browser"});
 await request("CreateInput",{sceneName:"Updates Browser",inputName:"Live Updates",inputKind:"browser_source",inputSettings:{is_local_file:true,local_file:path.join(folder,linkedMode?"index.html":"obs-probe.html"),width:160,height:100,shutdown:false,restart_when_active:false},sceneItemEnabled:true});
 async function waitLast(predicate){
  for(let i=0;i<120;i++){const rows=await samples().catch(()=>[]),last=rows.at(-1);if(last&&predicate(last))return last;await sleep(100);}
  throw new Error("OBS update state timeout: "+JSON.stringify((await samples()).at(-1)));
 }
 await request("SetCurrentPreviewScene",{sceneName:"Updates Browser"});
 await waitLast(state=>state.loaded&&state.revision===fixture.first.Revision);
 check(true,"actual OBS loads the fixed broadcast entry");
 const transitions=await request("GetSceneTransitionList");
 const cut=transitions.transitions.find(t=>t.transitionKind==="cut_transition");
 if(cut)await request("SetCurrentSceneTransition",{transitionName:cut.transitionName});
 await request("TriggerStudioModeTransition");
 const firstActivation=await waitLast(state=>state.programActive===true&&!state.waitingForProgram);
 check(firstActivation.time<.6,"actual OBS first transition restarts the loaded revision");
 await request("SetCurrentPreviewScene",{sceneName:"Updates Blank"});
 await request("TriggerStudioModeTransition");
 await waitLast(state=>state.waitingForProgram===true);
 check(true,"actual OBS preview waits for program activation through wrapper");
 await request("SetCurrentPreviewScene",{sceneName:"Updates Browser"});
 await request("TriggerStudioModeTransition");
 const restarted=await waitLast(state=>state.programActive===true&&!state.waitingForProgram);
 check(restarted.time<.6,"actual OBS program switch restarts the loaded revision"); let expectedRevision=fixture.second.Revision;
 if(linkedMode){
  const refreshed=spawnSync(path.join(root,".tools/dotnet/dotnet.exe"),[path.join(root,"tests/LiveCG.Verify/bin/Debug/net10.0/LiveCG.Verify.dll"),root,"--linked-refresh"],{encoding:"utf8",windowsHide:true});
  if(refreshed.status!==0)throw new Error(refreshed.stderr+" "+refreshed.stdout);
  const saved=JSON.parse(refreshed.stdout);expectedRevision=saved.revision;
  check(saved.entry===fixture.entry&&expectedRevision!==fixture.first.Revision,"real save-refresh keeps the registered index path while OBS is active");
 }else{
  const pointer=path.join(folder,"pointer.js"),temporary=pointer+".obs-test.tmp";
  await fs.writeFile(temporary,"window.LiveCGUpdate("+JSON.stringify({revision:fixture.second.Revision,immediate:false,binding:{Active:fixture.second,Previous:fixture.first,Immediate:false}})+");");
  await fs.rename(temporary,pointer);
 }
 await waitLast(state=>state.revision===expectedRevision); check(true,"actual OBS changes revision without a source reload");
 await sleep(200);
 const shot=await request("GetSourceScreenshot",{sourceName:"Live Updates",imageFormat:"png",imageWidth:160,imageHeight:100});
 const actual=path.join(folder,"actual-obs-update.png");
 await fs.writeFile(actual,Buffer.from(shot.imageData.split(",")[1],"base64"));
 const pixels=spawnSync(path.join(root,".tools/dotnet/dotnet.exe"),[path.join(root,"tests/LiveCG.Verify/bin/Debug/net10.0/LiveCG.Verify.dll"),root,"--broadcast-pixel",actual],{encoding:"utf8",windowsHide:true});
 check(pixels.status===0,"actual OBS displays the replacement blue image pixels");
 if(linkedMode){
  let scenePath;
  for(let attempt=0;attempt<80&&!scenePath;attempt++){
   for(const file of await fs.readdir(path.join(config,"basic/scenes"))){
    if(!file.endsWith(".json"))continue;
    const candidate=path.join(config,"basic/scenes",file);
    try{if(JSON.parse(await fs.readFile(candidate,"utf8")).name===collectionName){scenePath=candidate;break;}}catch{}
   }
   if(!scenePath)await sleep(100);
  }
  if(!scenePath)throw new Error("Actual OBS scene was not saved");
  const relocated=spawnSync(path.join(root,".tools/dotnet/dotnet.exe"),[path.join(root,"tests/LiveCG.Verify/bin/Debug/net10.0/LiveCG.Verify.dll"),root,"--linked-scene",scenePath],{encoding:"utf8",windowsHide:true});
  if(relocated.status!==0)throw new Error(relocated.stderr+" "+relocated.stdout);
  const migration=JSON.parse(relocated.stdout);
  check(migration.changed>0,"actual OBS scene JSON reconnects the browser path after relocation");
  await request("SetInputSettings",{inputName:"Live Updates",inputSettings:{local_file:migration.entry},overlay:true});
  await sleep(1500);
  const migratedShot=await request("GetSourceScreenshot",{sourceName:"Live Updates",imageFormat:"png",imageWidth:160,imageHeight:100});
  const migratedPng=path.join(folder,"actual-obs-relocated.png");
  await fs.writeFile(migratedPng,Buffer.from(migratedShot.imageData.split(",")[1],"base64"));
  const migratedPixels=spawnSync(path.join(root,".tools/dotnet/dotnet.exe"),[path.join(root,"tests/LiveCG.Verify/bin/Debug/net10.0/LiveCG.Verify.dll"),root,"--broadcast-pixel",migratedPng],{encoding:"utf8",windowsHide:true});
  check(migratedPixels.status===0,"actual OBS renders the rebound scene from the moved folder");
 }
}
try{
    await connect();let version;for(let i=0;i<150;i++){try{version=await request('GetVersion');break;}catch(error){if(!error.message.includes('207'))throw error;await sleep(100);}}if(!version)throw new Error('OBS frontend initialization did not finish');check(version.obsVersion.startsWith('33.'),'isolated actual OBS 33 started with test-only profile');
    if(updateMode){await verifyBroadcastUpdates();}else{
    await request('CreateSceneCollection',{sceneCollectionName:'LiveCGProbe-'+Date.now()});
    for(const sceneName of ['LiveCG Blank','LiveCG Browser'])await request('CreateScene',{sceneName});
    const mode=await request('GetStudioModeEnabled');if(!mode.studioModeEnabled)await request('SetStudioModeEnabled',{studioModeEnabled:true});await request('SetCurrentProgramScene',{sceneName:'LiveCG Blank'});
    const inputName='LiveCG Program Probe';try{await request('RemoveInput',{inputName});}catch{}
    const size=digitPreview?JSON.parse(await fs.readFile(path.join(folder,'manifest.json'),'utf8')):{width:160,height:100};
    await request('CreateInput',{sceneName:'LiveCG Browser',inputName,inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(folder,'obs-probe.html'),width:size.width,height:size.height,fps_custom:true,fps:30,shutdown:false,restart_when_active:false},sceneItemEnabled:true});
    await request('SetCurrentPreviewScene',{sceneName:'LiveCG Browser'});
    let rows=await waitProbe(s=>s.loaded);await sleep(700);rows=await samples();const last=rows.at(-1);const sourceState=await request('GetSourceActive',{sourceName:inputName});
    check(sourceState.videoShowing&&!sourceState.videoActive&&last?.loaded,'actual OBS preview is showing but not program-active before transition');
    if(digitPreview)check(last?.originalPrice===true,'parked OBS preview displays original price pixels');
    const transitions=await request('GetSceneTransitionList');const cut=transitions.transitions.find(t=>t.transitionKind==='cut_transition');if(cut)await request('SetCurrentSceneTransition',{transitionName:cut.transitionName});
    const before=rows.length;await request('TriggerStudioModeTransition');rows=await waitProbe((s,i)=>s.programActive===true&&s.time<.3);await sleep(350);rows=await samples();const shown=rows.slice(before).filter(s=>s.programActive===true);
    check(shown.length>0&&shown[0].time<.3&&!shown[0].waitingForProgram,'actual preview-to-program transition starts entrance from frame zero');
    if(digitPreview)check(shown.some(s=>s.originalPrice===false),'program displays rolling price pixels');
    await sleep(500);rows=await samples();check(rows.at(-1)?.time>.4,'actual program playback advances after the first entrance frame');
    if(digitPreview){
        rows=await waitProbe(s=>s.programActive&&s.phase==='완료'&&s.originalPrice===true);
        check(rows.some(s=>s.programActive&&s.phase==='완료'&&s.originalPrice===true),'program stops on original price pixels');
    }
    await request('SetCurrentPreviewScene',{sceneName:'LiveCG Blank'});await request('TriggerStudioModeTransition');await sleep(400);rows=await samples();check(rows.at(-1)?.waitingForProgram&&rows.at(-1)?.time<.01,'returning source to preview resets the waiting frame');
    if(digitPreview)check(rows.at(-1)?.originalPrice===true,'returning to preview shows the original price again');
    await request('SetCurrentPreviewScene',{sceneName:'LiveCG Browser'});await sleep(700);const start=(await samples()).length;await request('TriggerStudioModeTransition');await sleep(500);rows=(await samples()).slice(start).filter(s=>s.programActive===true);check(rows.length>0&&rows[0].time<.3,'second actual program transition replays entrance even after a long preview');
    if(!digitPreview){
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
    const textDir=path.join(root,'artifacts/v010-core/text-strokes.obs'),textManifest=JSON.parse(await fs.readFile(path.join(textDir,'manifest.json'),'utf8')),textHtml=await fs.readFile(path.join(textDir,textManifest.entry),'utf8');
    await fs.writeFile(path.join(textDir,'obs-text.html'),textHtml.replace('</body>',`<script>let ready=false;setInterval(()=>{if(window.LiveCG?.status().loaded){LiveCG.renderAt(.6);if(!ready){ready=true;console.error('[TEXT-STROKES]')}}},50)</script></body>`));
    await request('CreateInput',{sceneName:'LiveCG Browser',inputName:'Text Stroke Preservation',inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(textDir,'obs-text.html'),width:240,height:120,shutdown:false,restart_when_active:false},sceneItemEnabled:true});let textReady=false;for(let i=0;i<100;i++){if((await fs.readFile(logFile,'utf8')).includes('[TEXT-STROKES]')){textReady=true;break;}await sleep(100);}check(textReady,'actual OBS text matte is ready');await sleep(150);
    const textShot=await request('GetSourceScreenshot',{sourceName:'Text Stroke Preservation',imageFormat:'png',imageWidth:240,imageHeight:120}),textActual=path.join(textDir,'actual-obs-text.png');await fs.writeFile(textActual,Buffer.from(textShot.imageData.split(',')[1],'base64'));renderCaptures.push({package:'Actual OBS text strokes',premultiplied:true,time:.6,expected:path.join(textDir,'expected','0.60.png'),actual:textActual});check(textShot.imageData.startsWith('data:image/png;base64,'),'actual OBS text strokes captured for native comparison');
    for(const [style,time] of [['Solid',.5],['Dashed',2],['Dotted',3.5],['Arrows',5],['hidden',.5],['GroupMask',3.5],['Upright',2],['Vertical',2],['Bends',5],['ROI',5]]){
        const dir=path.join(root,'artifacts/v010-core/route-'+style+'.obs'),manifest=JSON.parse(await fs.readFile(path.join(dir,'manifest.json'),'utf8')),html=await fs.readFile(path.join(dir,manifest.entry),'utf8'),input='Travel Route '+style,marker='[TRAVEL-'+style+']';
        await fs.writeFile(path.join(dir,'obs-route.html'),html.replace('</body>',`<script>let ready=false;setInterval(()=>{if(window.LiveCG?.status().loaded){LiveCG.renderAt(${time});if(!ready){ready=true;console.error('${marker}')}}},50)</script></body>`));
        await request('CreateInput',{sceneName:'LiveCG Browser',inputName:input,inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(dir,'obs-route.html'),width:640,height:400,shutdown:false,restart_when_active:false},sceneItemEnabled:true});let ready=false;for(let i=0;i<100;i++){if((await fs.readFile(logFile,'utf8')).includes(marker)){ready=true;break;}await sleep(100);}check(ready,'actual OBS travel route ready '+style);await sleep(150);
        const shot=await request('GetSourceScreenshot',{sourceName:input,imageFormat:'png',imageWidth:640,imageHeight:400}),actual=path.join(dir,'actual-obs-route.png');await fs.writeFile(actual,Buffer.from(shot.imageData.split(',')[1],'base64'));renderCaptures.push({package:'Actual OBS route '+style,premultiplied:true,time,expected:path.join(dir,'expected',time.toFixed(2)+'.png'),actual});check(shot.imageData.startsWith('data:image/png;base64,'),'actual OBS travel route capture '+style);
    }
    for(const [name,time] of [['numeric-saved.obs',.2],['numeric-mask.obs',.2],['numeric-child.obs',.2],['numeric-bounce.obs',.2],['route-Stops.obs',.5],['route-Stops.obs',3.5],['route-Stops.obs',7],['redraw-price.obs',0],['redraw-manual.obs',0],...(process.env.LIVECG_TEST_PRICE_IMAGE?[['local-redraw.obs',0]]:[])]){
        const dir=path.join(root,'artifacts/v010-core',name),manifest=JSON.parse(await fs.readFile(path.join(dir,'manifest.json'),'utf8')),html=await fs.readFile(path.join(dir,manifest.entry),'utf8'),id=name+'-'+time,marker='[V122-'+id+']',probe='obs-v122-'+time+'.html';
        await fs.writeFile(path.join(dir,probe),html.replace('</body>',`<script>let ready=false;setInterval(()=>{if(window.LiveCG?.status().loaded){LiveCG.renderAt(${time});if(!ready){ready=true;console.error('${marker}')}}},50)</script></body>`));
        await request('CreateInput',{sceneName:'LiveCG Browser',inputName:id,inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(dir,probe),width:manifest.width,height:manifest.height,shutdown:false,restart_when_active:false},sceneItemEnabled:true});let ready=false;for(let i=0;i<100;i++){if((await fs.readFile(logFile,'utf8')).includes(marker)){ready=true;break;}await sleep(100);}check(ready,'actual OBS v122 ready '+id);await sleep(150);
        let shot;for(let frame=0;frame<30;frame++){shot=await request('GetSourceScreenshot',{sourceName:id,imageFormat:'png',imageWidth:manifest.width,imageHeight:manifest.height});if(shot.imageData.length>1000)break;await sleep(50);}const actual=path.join(dir,'actual-obs-v122-'+time+'.png');await fs.writeFile(actual,Buffer.from(shot.imageData.split(',')[1],'base64'));renderCaptures.push({package:'Actual OBS '+id,premultiplied:true,time,expected:path.join(dir,'expected',time.toFixed(2)+'.png'),actual});check(shot.imageData.startsWith('data:image/png;base64,')&&shot.imageData.length>1000,'actual OBS v122 capture '+id);
    }
    const restDir=path.join(root,'artifacts/v010-core/v1-rest.obs'),restManifest=JSON.parse(await fs.readFile(path.join(restDir,'manifest.json'),'utf8')),restHtml=await fs.readFile(path.join(restDir,restManifest.entry),'utf8');
    const restProbe=restHtml.replace('</body>',`<script>let ready=false;setInterval(()=>{if(window.LiveCG?.status().loaded){LiveCG.renderElapsedAt(1.8);if(!ready){ready=true;console.error('[V1-REST] '+JSON.stringify(LiveCG.status()))}}},50)</script></body>`);
    await fs.writeFile(path.join(restDir,'obs-rest.html'),restProbe);await request('CreateInput',{sceneName:'LiveCG Browser',inputName:'V1 Original Rest',inputKind:'browser_source',inputSettings:{is_local_file:true,local_file:path.join(restDir,'obs-rest.html'),width:160,height:100,shutdown:false,restart_when_active:false},sceneItemEnabled:true});
    let restReady=false;for(let i=0;i<100;i++){if((await fs.readFile(logFile,'utf8')).includes('[V1-REST]')){restReady=true;break;}await sleep(100);}check(restReady,'actual OBS loads named-entry package and reaches the rest phase');await sleep(150);
    const restShot=await request('GetSourceScreenshot',{sourceName:'V1 Original Rest',imageFormat:'png',imageWidth:160,imageHeight:100}),restActual=path.join(restDir,'actual-obs-rest.png');await fs.writeFile(restActual,Buffer.from(restShot.imageData.split(',')[1],'base64'));renderCaptures.push({package:'Actual OBS v1 original rest',premultiplied:true,time:1.8,expected:path.join(restDir,'original.png'),actual:restActual});check(restShot.imageData.startsWith('data:image/png;base64,'),'actual OBS rest captured for exact source comparison');
}}}catch(error){check(false,error.stack);}
finally{if(ws)ws.close();await fs.writeFile(path.join(root,'artifacts/v010-obs-results.json'),JSON.stringify({passed:results.filter(r=>r.ok).length,total:results.length,log:logFile,results},null,2));await fs.writeFile(path.join(root,'artifacts/v010-obs-render-captures.json'),JSON.stringify(renderCaptures,null,2));child.kill();}
console.log(`RESULT ${results.filter(r=>r.ok).length}/${results.length} passed`);process.exitCode=results.some(r=>!r.ok)?1:0;
