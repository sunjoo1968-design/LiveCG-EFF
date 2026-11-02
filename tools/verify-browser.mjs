// Actual Chromium/CEF-family regression checks without a server or external JS dependencies.
import { spawn } from "node:child_process";
import fs from "node:fs/promises";
import path from "node:path";
import { pathToFileURL } from "node:url";

const root = path.resolve(process.argv[2] ?? ".");
const folder = path.join(root, "artifacts/v010-core"), results = [], captures = [];
const chrome = process.env.LIVECG_TEST_CHROME ?? "C:/Program Files/Google/Chrome/Application/chrome.exe";
const profile = path.join(root, "artifacts/v010-browser-profile");
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
async function load(name, preview = false) {
    const packageDir = path.join(folder, name), manifest = JSON.parse((await fs.readFile(path.join(packageDir, "manifest.json"), "utf8")).replace(/^\uFEFF/, ""));
    await send("Emulation.setDeviceMetricsOverride", { width: manifest.width, height: manifest.height, deviceScaleFactor: 1, mobile: false }, session);
    await send("Emulation.setDefaultBackgroundColorOverride", { color: { r: 0, g: 0, b: 0, a: 0 } }, session);
    await send("Page.navigate", { url: pathToFileURL(path.join(packageDir, preview ? (manifest.preview??"preview.html") : (manifest.entry??"index.html"))).href }, session);
    let state;
    for (let i = 0; i < 100; i++) { state = await evaluate("window.LiveCG?.status()").catch(() => null); if (state?.loaded || state?.error) break; await sleep(50); }
    check(state?.loaded && !state.error, name + " loads via local file with all PNGs and no runtime error");
    if (!state?.loaded || state.error) throw new Error(JSON.stringify(state));
    return { packageDir, manifest };
}
try {
    const target = await send("Target.createTarget", { url: "about:blank" }); session = (await send("Target.attachToTarget", { targetId: target.targetId, flatten: true })).sessionId;
    await send("Page.enable", {}, session); await send("Runtime.enable", {}, session);
    for (const name of ["한글 로컬 & 패키지.obs", "all-effects.obs", "group-masks.obs", "paths-cutout.obs", "broadcast-polish.obs","soft-inside.obs","soft-outside.obs","soft-both.obs","content-only.obs","soft-group.obs","soft-numeric.obs","manual-separation.obs","inactive-manual-group.obs","text-strokes.obs","route-Solid.obs","route-Dashed.obs","route-Dotted.obs","route-Arrows.obs","route-hidden.obs","route-GroupMask.obs","route-Upright.obs","route-Vertical.obs","route-Bends.obs","route-ROI.obs","numeric-saved.obs","numeric-mask.obs","numeric-child.obs","numeric-bounce.obs","route-Stops.obs"]) {
        const { packageDir } = await load(name);
        const expected = await fs.readdir(path.join(packageDir, "expected"));
        for (const file of expected.filter(f => f.endsWith(".png"))) {
            const time = Number(file.slice(0, -4)); await evaluate(`LiveCG.renderAt(${time})`); await sleep(70);
            const shot = await send("Page.captureScreenshot", { format: "png", captureBeyondViewport: true }, session);
            const actual = path.join(packageDir, "actual-" + file); await fs.writeFile(actual, Buffer.from(shot.data, "base64"));
            captures.push({ package: name, time, expected: path.join(packageDir, "expected", file), actual });
            check(!(await evaluate("LiveCG.status().error")), name + " renders native comparison time " + time);
        }
    }
    await load("content-only.obs");await evaluate("LiveCG.restart();LiveCG.pause();LiveCG.exit()");check(await evaluate("!LiveCG.status().exitRequested"),"disabled exit leaves browser emphasis running");await evaluate("window.dispatchEvent(new CustomEvent('obsSourceActiveChanged',{detail:{active:false}}));window.dispatchEvent(new CustomEvent('obsSourceActiveChanged',{detail:{active:true}}))");check((await evaluate("LiveCG.status()")).time<.2,"effect-derived no-exit package restarts at actual program activation");
    await load("한글 로컬 & 패키지.obs"); await evaluate("LiveCG.restart()"); await sleep(2250);
    let status = await evaluate("LiveCG.status()"); check(status.phase === "반복" && status.time >= .5 && status.time < 1.5, "after entrance and multiple cycles only the emphasis section repeats");
    await evaluate("LiveCG.pause()"); const paused = (await evaluate("LiveCG.status()")).time; await sleep(100);check((await evaluate("LiveCG.status()")).time === paused, "pause freezes the playback clock");
    await evaluate("LiveCG.resume(); window.dispatchEvent(new CustomEvent('LiveCGControl',{detail:{command:'exit',target:'different.png'}}))");check(!(await evaluate("LiveCG.status().exitRequested")), "targeted command ignores a different PNG source");
    await evaluate("window.dispatchEvent(new KeyboardEvent('keydown',{code:'KeyE'}))");check(await evaluate("LiveCG.status().exitRequested"), "OBS interaction keyboard E requests exit");
    await sleep(1900);check((await evaluate("LiveCG.status()")).phase === "퇴장 완료", "requested exit completes and stops rather than repeating the entrance");
    await evaluate("window.dispatchEvent(new KeyboardEvent('keydown',{code:'KeyR'}))");check(!(await evaluate("LiveCG.status().exitRequested")), "OBS interaction keyboard R restarts a new entrance");
    await sleep(150);await evaluate("window.dispatchEvent(new CustomEvent('obsSourceVisibleChanged',{detail:{visible:false}}));window.dispatchEvent(new CustomEvent('obsSourceVisibleChanged',{detail:{visible:true}}))");check((await evaluate("LiveCG.status()")).time < .1, "OBS invisible-to-visible event restarts according to the saved policy");
    await sleep(100);await evaluate("window.dispatchEvent(new CustomEvent('obsSourceVisibleChanged',{detail:{visible:true}}))");check((await evaluate("LiveCG.status()")).time >= .09, "duplicate visible event does not interrupt the entrance");
    await evaluate("LIVECG_DATA.playback.replayOnShow=false;window.dispatchEvent(new CustomEvent('obsSourceVisibleChanged',{detail:{visible:false}}));window.dispatchEvent(new CustomEvent('obsSourceVisibleChanged',{detail:{visible:true}}))");check((await evaluate("LiveCG.status()")).time >= .09, "optional no-replay policy preserves playback when source returns");
    await evaluate("LIVECG_DATA.playback.exitAtLoopEnd=false;LiveCG.restart();LiveCG.pause();LiveCG.exit()");check((await evaluate("LiveCG.status()")).time >= 1.5 && !(await evaluate("LiveCG.status().paused")), "immediate exit resumes paused playback and enters the exit section");
    const restPackage=await load("v1-rest.obs");check(restPackage.manifest.entry.endsWith("_index.html"),"named source HTML is the primary browser entry");
    check((await evaluate("LiveCG.renderElapsedAt(1.8)")).rest,"browser enters original rest after one emphasis pass");await sleep(80);
    const restShot=await send("Page.captureScreenshot",{format:"png",captureBeyondViewport:true},session),restActual=path.join(restPackage.packageDir,"actual-rest.png");await fs.writeFile(restActual,Buffer.from(restShot.data,"base64"));captures.push({package:"v1 original rest",time:1.8,expected:path.join(restPackage.packageDir,"original.png"),actual:restActual});
    check(restShot.data.length>100,"browser original-rest screenshot captured for native pixel comparison");
    check(!(await evaluate("LiveCG.renderElapsedAt(2.5)")).rest&&(await evaluate("LiveCG.status()")).time===.5,"browser resumes emphasis after rest without replaying entrance");
    for(const mode of ["Transparent","Hold"]){await evaluate(`LIVECG_DATA.plan.restMode='${mode}';LiveCG.renderElapsedAt(1.8)`);await sleep(80);const shot=await send("Page.captureScreenshot",{format:"png",captureBeyondViewport:true},session),actual=path.join(restPackage.packageDir,"actual-"+mode+".png");await fs.writeFile(actual,Buffer.from(shot.data,"base64"));captures.push({package:"v1 "+mode+" rest",time:1.8,expected:path.join(restPackage.packageDir,mode+".png"),actual});check(shot.data.length>80,"browser "+mode+" rest captured for native comparison");}
    await evaluate("LIVECG_DATA.plan.restMode='Hold';LiveCG.renderElapsedAt(1.8)");check((await evaluate("LiveCG.status()")).time===1.4,"browser hold rest retains the final emphasis frame");
    for(const name of ["v1/Jpeg.obs","v1/Webp.obs"]){await load(name);check(await evaluate("LiveCG.status().loaded&&!LiveCG.status().error"),name+" renders normalized PNG assets");}
    await load("한글 로컬 & 패키지.obs", true);check(await evaluate("document.getElementById('credit').textContent.includes('SunjooAn')&&document.querySelectorAll('button').length===3"), "separate preview exposes creator/version and live controls");
} catch (error) { check(false, error.stack); }
finally {
    await fs.writeFile(path.join(root, "artifacts/v010-browser-captures.json"), JSON.stringify(captures, null, 2));
    await fs.writeFile(path.join(root, "artifacts/v010-browser-results.json"), JSON.stringify({ passed: results.filter(r => r.ok).length, total: results.length, results }, null, 2));
    await send("Browser.close").catch(() => {}); if (child.exitCode === null) child.kill();
}
console.log(`RESULT ${results.filter(r => r.ok).length}/${results.length} passed`);
process.exitCode = results.some(r => !r.ok) ? 1 : 0;
