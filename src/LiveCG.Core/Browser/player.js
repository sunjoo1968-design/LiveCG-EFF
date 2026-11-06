/* LiveCG-EFF · SunjooAn. Local PNG/Canvas runtime; no network, server, or media loop. */
"use strict";
(() => {
    const data = window.LIVECG_DATA;
    const canvas = document.getElementById("livecg");
    const ctx = canvas.getContext("2d");
    canvas.width = data.width; canvas.height = data.height;
    const images = new Map(), buffers = new Map();
    const assets = new Map(data.assets.map(a => [a.id, a]));
    const plan = data.plan, fps = data.fps;
    const A = plan.introFrames / fps, B = plan.loopEndFrame / fps, END = plan.endFrame / fps, LOOP = B - A;
    const REST=(plan.restFrames??0)/fps, CYCLE=LOOP+REST;
    const entrances = new Set(["FadeIn", "RiseIn", "ZoomIn", "ZoomOutIn"]);
    const needsLayer = k => entrances.has(k) || ["Zoom", "Bounce", "Shake", "Float", "DigitRoll", "UserMotion"].includes(k);
    const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
    const image = name => images.get(name);
    const power = c => ({VeryWeak:.18,Weak:.55,Medium:1,Strong:1.65,VeryStrong:2.4}[c.options.strength] ?? 1);
    const holds = k => k === "Highlight" || k === "Underline" || k === "TravelRoute";
    entrances.add("WipeRight"); entrances.add("FallIn");
    const blank = (key, w, h) => {
        let c = buffers.get(key);
        if (!c) { c = document.createElement("canvas"); buffers.set(key, c); }
        if (c.width !== w || c.height !== h) { c.width = w; c.height = h; }
        const x = c.getContext("2d"); x.resetTransform(); x.globalAlpha = 1; x.globalCompositeOperation = "source-over";
        x.filter = "none"; x.clearRect(0, 0, w, h); return c;
    };
    const copy = (key, source) => { const c = blank(key, source.width, source.height); c.getContext("2d").drawImage(source, 0, 0); return c; };
    const multiply = (target, mask) => { const x = target.getContext("2d"); x.save(); x.globalCompositeOperation = "destination-in"; x.drawImage(mask, 0, 0); x.restore(); };
    function shape(r, b) {
        const p = new Path2D(), { x, y, width: w, height: h } = b;
        if (r.shape === "Ellipse") p.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
        else if (r.shape === "RoundedRectangle") p.roundRect(x, y, w, h, Math.min(w, h) * .18);
        else if ((r.shape === "Freehand" || r.shape === "Polygon") && r.points.length > 2) {
            r.points.forEach((v, i) => i ? p.lineTo(x + v.x * w, y + v.y * h) : p.moveTo(x + v.x * w, y + v.y * h)); p.closePath();
        } else p.rect(x, y, w, h);
        return p;
    }
    function effective(clips, time) {
        const list = clips.filter(c => c.enabled).slice().sort((a, b) => a.start - b.start), chosen = new Map();
        for (const c of list) if (c.sequenceId && (!chosen.has(c.sequenceId) || c.start <= time)) chosen.set(c.sequenceId, c.id);
        return list.filter(c => !c.sequenceId || chosen.get(c.sequenceId) === c.id);
    }
    function progress(c, time) {
        const elapsed = clamp(time - c.start, 0, c.duration) + c.sourceOffset, total = c.sourceDuration ?? c.duration;
        if (elapsed >= total - 1e-10) return 1;
        if (!c.loop) return clamp(elapsed / total);
        const cycle = elapsed / Math.max(.016, c.loopPeriod); return Math.max(0, cycle - Math.floor(cycle + 1e-10));
    }
    function value(m, property, frame) {
        const keys = m?.tracks.find(t => t.property === property)?.keys.slice().sort((a, b) => a.frame - b.frame);
        const def = ["Opacity", "ScaleX", "ScaleY"].includes(property) ? 1 : property.startsWith("Anchor") ? .5 : 0;
        if (!keys?.length) return def;
        if (frame <= keys[0].frame) return keys[0].value;
        for (let i = 1; i < keys.length; i++) if (frame < keys[i].frame) {
            const a = keys[i - 1], b = keys[i]; let t = (frame - a.frame) / (b.frame - a.frame);
            if (a.easing === "EaseIn") t *= t;
            else if (a.easing === "EaseOut") t = 1 - (1 - t) ** 2;
            else if (a.easing === "EaseInOut") t = t * t * (3 - 2 * t);
            else if (a.easing === "Hold") t = 0;
            return a.value + (b.value - a.value) * t;
        }
        return keys.at(-1).value;
    }
    function motion(clips, time, bounds, digitBounce) {
        const s = { alpha: 1, scale: 1, sx: 1, sy: 1, ax: .5, ay: .5, x: 0, y: 0, angle: 0, top:0,bottom:0,left:0,right:0 };
        const first = clips.find(c => entrances.has(c.options.kind) || c.options.kind === "UserMotion" && value(c.motion, "Opacity", 0) === 0);
        const user = clips.filter(c => c.options.kind === "UserMotion" && c.motion && time >= c.start).at(-1);
        if (user) {
            const f = progress(user, time) * user.motion.lengthFrames, m = user.motion;
            s.x = value(m, "X", f) * bounds.width; s.y = value(m, "Y", f) * bounds.height;
            s.sx = value(m, "ScaleX", f); s.sy = value(m, "ScaleY", f); s.angle = value(m, "Rotation", f);
            s.alpha = value(m, "Opacity", f); s.ax = value(m, "AnchorX", f); s.ay = value(m, "AnchorY", f);
            s.top=value(m,"CropTop",f);s.bottom=value(m,"CropBottom",f);s.left=value(m,"CropLeft",f);s.right=value(m,"CropRight",f);
        }
        if (first && time < first.start) s.alpha = 0;
        for (const c of clips) {
            if (time < c.start) continue;
            const p = progress(c, time), k = c.options.kind, pow = power(c), speed = c.options.speed, envelope = Math.sin(Math.PI * p) ** 2;
            if (entrances.has(k)) {
                const ease = 1 - (1 - p ** (1 / speed)) ** 3; if(k!=="WipeRight")s.alpha *= ease;
                if (k === "RiseIn") s.y += (1 - ease) * bounds.height * (.65 + .4 * pow);
                if (k === "FallIn") s.y -= (1 - ease) * bounds.height * (.65 + .4 * pow);
                if (k === "WipeRight") s.right=Math.max(s.right,1-ease);
                if (k === "ZoomIn") s.scale *= .12 + .88 * ease;
                if (k === "ZoomOutIn") s.scale *= 1 + (1 - ease) * (.4 + .45 * pow);
            } else if (time < c.start + c.duration) {
                if (k === "Zoom") s.scale *= 1 + .23 * pow * Math.sin(p * Math.PI * speed) ** 2;
                if (k === "Bounce" && !digitBounce) s.y -= Math.abs(Math.sin(p * Math.PI * 3 * speed)) * bounds.height * .32 * pow * envelope;
                if (k === "Shake") { s.angle += Math.sin(p * Math.PI * 14 * speed) * 8 * pow * envelope; s.x += Math.sin(p * Math.PI * 20 * speed) * bounds.width * .035 * pow * envelope; }
                if (k === "Float") { s.y -= Math.sin(p * Math.PI * 2 * speed) * bounds.height * .22 * pow * envelope; s.angle += Math.sin(p * Math.PI * 2 * speed) * 2 * pow * envelope; }
            }
        }
        return s;
    }
    const identity = s => s.alpha === 1 && s.scale === 1 && s.sx === 1 && s.sy === 1 && s.x === 0 && s.y === 0 && s.angle === 0 && !s.top&&!s.bottom&&!s.left&&!s.right;
    function crop(x,b,s){if(s.top||s.bottom||s.left||s.right){x.beginPath();x.rect(b.x+b.width*s.left,b.y+b.height*s.top,Math.max(0,b.width*(1-s.left-s.right)),Math.max(0,b.height*(1-s.top-s.bottom)));x.clip();}}
    function transform(x, b, s) {
        const ax = b.x + b.width * s.ax, ay = b.y + b.height * s.ay;
        x.translate(ax + s.x, ay + s.y); x.rotate(s.angle * Math.PI / 180); x.scale(s.scale * s.sx, s.scale * s.sy); x.translate(-ax, -ay);
    }
    const local = a => ({ x: 0, y: 0, width: a.width, height: a.height });
    function maskCoverage(r, parent, time, key) {
        const active = r.masks.filter(m => m.enabled && !(m.startAtInPoint && time < m.start) && !(time >= m.start + m.duration && m.endMode === "Release"));
        const result = blank(key, parent.width, parent.height), x = result.getContext("2d");
        if (!active.length || active[0].operation !== "Add") { x.fillStyle = "white"; x.fillRect(0, 0, result.width, result.height); }
        for (const m of active) {
            const one = blank(key + "-one", parent.width, parent.height), c = one.getContext("2d");
            if (time >= m.start && !(time >= m.start + m.duration && m.endMode === "Zero")) {
                const f = clamp((time - m.start) / m.duration) * (m.motion?.lengthFrames ?? 1), b = { x: m.bounds.x * data.width - parent.x, y: m.bounds.y * data.height - parent.y, width: m.bounds.width * data.width, height: m.bounds.height * data.height };
                const s = { x: value(m.motion, "X", f) * b.width, y: value(m.motion, "Y", f) * b.height, angle: value(m.motion, "Rotation", f), scale: 1, sx: value(m.motion, "ScaleX", f), sy: value(m.motion, "ScaleY", f), ax: value(m.motion, "AnchorX", f), ay: value(m.motion, "AnchorY", f) };
                s.top=value(m.motion,"CropTop",f);s.bottom=value(m.motion,"CropBottom",f);s.left=value(m.motion,"CropLeft",f);s.right=value(m.motion,"CropRight",f);c.save(); transform(c, b, s);crop(c,b,s); c.drawImage(image(assets.get(r.id).masks.find(a => a.id === m.id).image), 0, 0); c.restore();
                if (m.invert) { const old = copy(key + "-invert", one); c.fillStyle = "white"; c.fillRect(0, 0, one.width, one.height); c.globalCompositeOperation = "destination-out"; c.drawImage(old, 0, 0); c.globalCompositeOperation = "source-over"; }
                c.globalCompositeOperation = "destination-in"; c.globalAlpha = clamp(m.opacity * value(m.motion, "Opacity", f)); c.fillStyle = "white"; c.fillRect(0, 0, one.width, one.height); c.globalAlpha = 1;
            }
            x.globalCompositeOperation = m.operation === "Subtract" ? "destination-out" : m.operation === "Intersect" ? "destination-in" : "source-over"; x.drawImage(one, 0, 0);
        }
        x.globalCompositeOperation = "source-over"; return result;
    }
    function drawRoute(x,r,w,h,clip,p,opacity){
        const route=clip.route,compiled=assets.get(r.id).routes?.find(a=>a.id===clip.id);if(!route||!compiled)return;
        const points=route.points,segments=compiled.segments,duration=points.reduce((s,v,i)=>s+v.stop+(i?v.move:0),0),seconds=clamp(p)*duration;
        let sample={x:points[0].x,y:points[0].y,angle:segments.length?Math.atan2(segments[0].y[1]-points[0].y,segments[0].x[1]-points[0].x):0,distance:0,point:0,stopped:true,icon:points[0].icon??route.icon},completed=0,icon=sample.icon;
        if(seconds>=points[0].stop)for(let i=0;i<segments.length;i++){
            const s=segments[i],point=points[i+1];icon=points[i].icon??icon;
            if(seconds<s.start+s.duration){const f=clamp((seconds-s.start)/s.duration),d=f*s.length;let j=1;while(j<s.distance.length-1&&s.distance[j]<d)j++;const mix=(d-s.distance[j-1])/Math.max(1e-12,s.distance[j]-s.distance[j-1]);sample={x:s.x[j-1]+(s.x[j]-s.x[j-1])*mix,y:s.y[j-1]+(s.y[j]-s.y[j-1])*mix,angle:Math.atan2(s.y[j]-s.y[j-1],s.x[j]-s.x[j-1]),distance:completed+d,point:i,stopped:false,icon};break;}
            completed+=s.length;if(seconds<s.start+s.duration+point.stop||i===segments.length-1){sample={x:point.x,y:point.y,angle:Math.atan2(s.y.at(-1)-s.y.at(-2),s.x.at(-1)-s.x.at(-2)),distance:completed,point:i+1,stopped:true,icon:point.icon??icon};break;}
        }
        const full=new Path2D(),past=new Path2D();let used=0;
        for(const s of segments){full.moveTo(s.x[0]*w,s.y[0]*h);past.moveTo(s.x[0]*w,s.y[0]*h);for(let j=1;j<s.x.length;j++){full.lineTo(s.x[j]*w,s.y[j]*h);if(used+s.distance[j]<=sample.distance)past.lineTo(s.x[j]*w,s.y[j]*h);else if(used+s.distance[j-1]<sample.distance)past.lineTo(sample.x*w,sample.y*h);}used+=s.length;}
        x.save();x.lineCap='round';x.lineJoin='round';x.lineWidth=route.lineWidth;x.globalAlpha=route.opacity*opacity;
        if(route.futurePath){x.strokeStyle=compiled.color;x.globalAlpha*=60/255;x.stroke(full);x.globalAlpha=route.opacity*opacity;}
        if(route.outline>0){x.strokeStyle=compiled.outlineColor;x.lineWidth=route.lineWidth+route.outline*2;x.stroke(past);}x.lineWidth=route.lineWidth;x.strokeStyle=compiled.color;
        if(route.lineStyle==='Dashed')x.setLineDash([12,8]);else if(route.lineStyle==='Dotted')x.setLineDash([1,route.lineWidth*2]);x.stroke(past);x.setLineDash([]);
        if(route.lineStyle==='Arrows'){used=0;for(const s of segments){for(let j=8;j<s.x.length;j+=16){if(used+s.distance[j]>sample.distance)continue;x.save();x.translate(s.x[j]*w,s.y[j]*h);x.rotate(Math.atan2((s.y[j]-s.y[j-1])*h,(s.x[j]-s.x[j-1])*w));x.beginPath();x.moveTo(-7,-5);x.lineTo(0,0);x.lineTo(-7,5);x.stroke();x.restore();}used+=s.length;}}
        const stopStart=sample.point===0?0:segments[sample.point-1].start+segments[sample.point-1].duration;
        if(route.arrival&&sample.stopped&&points[sample.point].stop>0&&seconds>=stopStart&&seconds<stopStart+points[sample.point].stop){x.globalAlpha=200/255*route.opacity*opacity;x.lineWidth=2;x.beginPath();x.arc(sample.x*w,sample.y*h,route.iconSize*.65,0,Math.PI*2);x.stroke();}
        x.globalAlpha=opacity;x.translate(sample.x*w,sample.y*h);const angle=Math.atan2(Math.sin(sample.angle)*h,Math.cos(sample.angle)*w),left=Math.cos(angle)<-1e-9;let rotation=0,flip=false;if(route.direction==='Horizontal')flip=left;else if(route.direction==='Follow'){rotation=angle;if(left&&Math.abs(Math.cos(angle))>=Math.abs(Math.sin(angle))-1e-9){rotation+=angle>0?-Math.PI:Math.PI;flip=true;}}x.rotate(rotation);if(flip)x.scale(-1,1);
        if(route.animate&&!sample.stopped){x.translate(0,Math.sin(p*duration*8)*2);x.rotate(Math.sin(p*duration*6)*3*Math.PI/180);}const sprite=image(compiled.icons[sample.icon]);const scale=Math.min(route.iconSize/sprite.width,route.iconSize/sprite.height);x.drawImage(sprite,-sprite.width*scale/2,-sprite.height*scale/2,sprite.width*scale,sprite.height*scale);x.restore();
    }
    function decoration(x, r, w, h, c, p, opacity, alpha = null) {
        if(c.options.kind==='TravelRoute'){drawRoute(x,r,w,h,c,p,opacity);return;}alpha??=image(assets.get(r.id).image);
        const pow = power(c), edge = clamp(Math.min(p, 1 - p) * 9), amplitude = Math.min(1, .65 * pow) * opacity * edge;
        const outline = shape(r, { x: 0, y: 0, width: w, height: h }), k = c.options.kind;
        x.save(); x.lineCap = "butt";
        if (k === "Sweep") {
            const surface=blank(r.id+"-sweep",Math.ceil(w),Math.ceil(h)), d=surface.getContext("2d");d.save();d.clip(outline,"evenodd");const at=(-.4+1.8*p)*w,g=d.createLinearGradient(at-w*.14,0,at+w*.14,h);
            g.addColorStop(0,tint(c,255,255,255,0));g.addColorStop(.5,tint(c,255,255,255,245/255*amplitude));g.addColorStop(1,tint(c,255,255,255,0));d.fillStyle=g;d.fillRect(0,0,w,h);d.restore();if(alpha)multiply(surface,alpha);x.drawImage(surface,0,0,w,h);
        } else if (k === "Glow") {
            x.strokeStyle = tint(c,255,218,88,240/255*amplitude); x.lineWidth = Math.max(3, h * .07 * pow);
            x.filter = `blur(${Math.max(3, h * .15 * pow)}px)`; x.stroke(outline); x.filter = "none"; x.lineWidth = Math.max(2, h * .025); x.stroke(outline);
        } else if (k === "Highlight") {
            x.clip(outline, "evenodd"); x.globalCompositeOperation = "multiply"; x.fillStyle = tint(c,255,225,32,Math.min(210,120*pow)/255*opacity);
            const pth = new Path2D(); pth.roundRect(-w * .02, h * .3, w * 1.04 * (1 - (1 - p) ** 3), h * .65, h * .12); x.fill(pth);
        } else if(k === "Underline") {
            x.fillStyle=tint(c,255,225,32,Math.min(210,120*pow)/255*opacity);const pth=new Path2D();pth.roundRect(-w*.03,h*.86,w*1.06*(1-(1-p)**3),Math.max(2,h*.10),h*.05);x.fill(pth);
        } else if (k === "Sparkle") {
            x.fillStyle = tint(c,255,247,192,amplitude);
            for (let i = 0; i < 8; i++) {
                const sx = w * (i % 4) / 3, sy = i < 4 ? -h * .08 : h * 1.08, size = h * .22 * pow * (.4 + .6 * Math.abs(Math.sin(p * 9 + i))) * edge;
                const star = new Path2D(); [[0, -1], [.23, -.23], [1, 0], [.23, .23], [0, 1], [-.23, .23], [-1, 0], [-.23, -.23]].forEach((v, j) => j ? star.lineTo(sx + v[0] * size, sy + v[1] * size) : star.moveTo(sx, sy - size)); star.closePath(); x.fill(star);
            }
        } else if (k === "Border") {
            const g = x.createConicGradient(p * Math.PI * 2, w / 2, h / 2); g.addColorStop(0, tint(c,255,211,67,amplitude)); g.addColorStop(.3, tint(c,255,255,255,amplitude)); g.addColorStop(1, tint(c,255,211,67,.117647)); x.strokeStyle = g; x.lineWidth = Math.max(3, h * .065 * pow); x.stroke(outline);
        } else if (k === "Ripple") {
            x.strokeStyle = tint(c,255,225,120,240/255*(1-p)*amplitude); x.lineWidth = Math.max(4, Math.min(w, h) * .10 * pow);
            x.beginPath(); x.arc(w / 2, h / 2, Math.hypot(w, h) * .5 + p * Math.min(w, h) * .65 * pow, 0, Math.PI * 2); x.stroke();
        } else if (k === "Rays") {
            x.lineWidth = Math.max(2, h * .04 * pow); x.lineCap = "round"; x.strokeStyle = tint(c,255,229,99,amplitude);
            for (let i = 0; i < 12; i++) { const a = i * Math.PI / 6, spread = .15 + p * .45 * pow, px = w / 2 + Math.cos(a) * w * (.5 + spread), py = h / 2 + Math.sin(a) * h * (.5 + spread); x.beginPath(); x.moveTo(px, py); x.lineTo(px + Math.cos(a) * h * .35 * pow, py + Math.sin(a) * h * .35 * pow); x.stroke(); }
        } else if (k === "OrbitBurst") {
            for (let i = 0; i < 10; i++) { const a = i * Math.PI / 5 + p * Math.PI * 2 * c.options.speed, radius = .45 + p * .7 * pow; x.fillStyle = i % 2 ? tint(c,112,243,222,240/255*amplitude) : tint(c,255,230,78,240/255*amplitude); x.beginPath(); x.arc(w / 2 + Math.cos(a) * w * radius, h / 2 + Math.sin(a) * h * radius, Math.max(2, h * .085 * pow * (1 - p * .6)), 0, Math.PI * 2); x.fill(); }
        }
        x.restore();
    }
    function tint(c,r,g,b,a){const s=c.options.color;if(s){const hex=s.replace('#','');if(hex.length===8){a*=parseInt(hex.slice(0,2),16)/255;r=parseInt(hex.slice(2,4),16);g=parseInt(hex.slice(4,6),16);b=parseInt(hex.slice(6,8),16);}else if(hex.length===6){r=parseInt(hex.slice(0,2),16);g=parseInt(hex.slice(2,4),16);b=parseInt(hex.slice(4,6),16);}}return `rgba(${r},${g},${b},${clamp(a)})`;}
    function digits(x, r, a, clips, time, prepared = null) {
        const roll = clips.filter(c => c.options.kind === "DigitRoll" && time >= c.start && time < c.start + c.duration).at(-1);
        const bounce = clips.filter(c => c.options.kind === "Bounce" && time >= c.start && time < c.start + c.duration).at(-1);
        if ((!roll && !bounce) || !a.digits.length) return false;
        const foreground = prepared?.image ?? image(a.image), backing = prepared?.backing ?? image(a.backing);
        x.save(); if (roll) x.clip(shape(r, local(a)), "evenodd"); x.drawImage(backing, 0, 0); x.fillStyle = a.ink; x.textAlign = "left"; x.textBaseline = "alphabetic";
        a.digits.forEach((cell, i) => {
            const p = bounce ? progress(bounce, time) : 0, delayed = clamp((p - i / a.digits.length * .35) / .65), offset = bounce ? -Math.sin(delayed * Math.PI) * cell.height * .75 * power(bounce) : 0;
            const y = cell.y + offset;
            if (!roll || progress(roll, time) >= .5 + .48 * (i + 1) / a.digits.length) x.drawImage(foreground, cell.x, cell.y, cell.width, cell.height, cell.x, y, cell.width, cell.height);
            else {
                const turns = (1 - (1 - progress(roll, time)) ** 2) * 45 * roll.options.speed, tick = Math.floor(turns), number = (((data.seeds[roll.id] + i * 17 + tick * 71) >>> 0) % 10), shift = (turns - tick) * cell.height;
                const slot=a.rollSlots?.[i]??cell;x.save();x.beginPath();x.rect(slot.x,slot.y+offset,slot.width,slot.height);x.clip();
                for (let row = -1; row <= 0; row++) {
                    const value=(number-row+10)%10,glyph=a.rollGlyphs?.[i];
                    if(glyph){x.drawImage(image(glyph.image),0,value*glyph.height,glyph.width,glyph.height,glyph.x,glyph.y+offset+shift+row*cell.height,glyph.width,glyph.height);continue;}
                    const text=String(value);x.font=`bold ${slot.fontSizes?.[value]??cell.height/.74}px Arial`;const metrics=x.measureText(text); const baseline = (metrics.actualBoundingBoxAscent - metrics.actualBoundingBoxDescent) / 2,center=(metrics.actualBoundingBoxRight-metrics.actualBoundingBoxLeft)/2; x.fillText(text, cell.x + cell.width / 2-center, y + cell.height / 2 + baseline + shift + row * cell.height);
                }
                x.restore();
            }
        }); x.restore(); return true;
    }
    function applyArea(frame, r, parent, time) {
        const a = assets.get(r.id), clips = effective(r.clips, time); if (!clips.length) return;
        if(a.customRaster||r.softness?.enabled||r.contentOnly){applySoftArea(frame,r,parent,time,a,clips);return;}
        const b = { x: r.bounds.x * data.width - parent.x, y: r.bounds.y * data.height - parent.y, width: r.bounds.width * data.width, height: r.bounds.height * data.height };
        const numeric = a.digits.length > 0 && clips.some(c => ["DigitRoll", "Bounce"].includes(c.options.kind) && time >= c.start && time < c.start + c.duration), s = motion(clips, time, b, numeric);
        const masks = maskCoverage(r, parent, time, r.id + "-masks");
        if (!identity(s) || numeric) {
            const old = copy(r.id + "-old", frame), effect = blank(r.id + "-effect", frame.width, frame.height), e = effect.getContext("2d");
            e.fillStyle = a.background; e.fillRect(0, 0, frame.width, frame.height);
            if (numeric) {
                // Sample the current parent, preserving earlier overlapping local effects.
                const ox = a.x - parent.x, oy = a.y - parent.y;
                const fg = blank(r.id + "-numeric-source", a.width, a.height), n = fg.getContext("2d");
                n.drawImage(old, ox, oy, a.width, a.height, 0, 0, a.width, a.height); multiply(fg, image(a.shape));
                const backing = copy(r.id + "-numeric-backing", fg), bk = backing.getContext("2d"); bk.save(); bk.clip(shape(r, local(a)), "evenodd"); bk.fillStyle = a.background; for (const cell of a.digits) { bk.clearRect(cell.x, cell.y, cell.width, cell.height); bk.fillRect(cell.x, cell.y, cell.width, cell.height); } bk.restore();
                e.clearRect(0, 0, effect.width, effect.height); e.drawImage(old, 0, 0); e.globalCompositeOperation = "destination-out"; e.drawImage(image(a.erase), ox, oy); e.globalCompositeOperation = "source-over"; e.drawImage(image(a.fill), ox, oy);
                e.save(); e.translate(ox, oy); transform(e, local(a), s);crop(e,local(a),s); e.globalAlpha = clamp(s.alpha); digits(e, r, a, clips, time, { image: fg, backing }); e.globalAlpha = 1;
                for (const c of clips) if (time >= c.start && (time < c.start + c.duration || holds(c.options.kind))) decoration(e, r, a.width, a.height, c, progress(c, time), s.alpha); e.restore();
            } else { e.save(); transform(e, b, s);crop(e,b,s); e.globalAlpha = clamp(s.alpha); e.drawImage(old, 0, 0); e.restore(); }
            const coverage = blank(r.id + "-coverage", frame.width, frame.height), c = coverage.getContext("2d"); c.fillStyle = "white";
            if (numeric) c.fillRect(0, 0, coverage.width, coverage.height);
            else { c.fill(shape(r, b), "evenodd"); c.save(); transform(c, b, s); c.fill(shape(r, b), "evenodd"); c.restore(); }
            multiply(coverage, masks); multiply(effect, coverage);
            const x = frame.getContext("2d"); x.globalCompositeOperation = "destination-out"; x.drawImage(coverage, 0, 0); x.globalCompositeOperation = "lighter"; x.drawImage(effect, 0, 0); x.globalCompositeOperation = "source-over";
        }
        if (numeric) return;
        const decor = blank(r.id + "-decor", frame.width, frame.height), d = decor.getContext("2d"); d.save(); transform(d, b, s);crop(d,b,s); d.translate(b.x, b.y);
        for (const c of clips) if (time >= c.start && (time < c.start + c.duration || holds(c.options.kind))) decoration(d, r, b.width, b.height, c, progress(c, time), s.alpha);
        d.restore(); multiply(decor, masks); frame.getContext("2d").drawImage(decor, 0, 0);
    }
    function applySoftArea(frame,r,parent,time,a,clips){
        if(r.overlayOnly){const layer=blank(r.id+'-overlay-only',parent.width,parent.height),c=layer.getContext('2d');c.translate(a.x-parent.x,a.y-parent.y);for(const clip of clips)if(time>=clip.start&&(time<clip.start+clip.duration||holds(clip.options.kind)))decoration(c,r,a.width,a.height,clip,progress(clip,time),1);multiply(layer,maskCoverage(r,parent,time,r.id+'-overlay-masks'));frame.getContext('2d').drawImage(layer,0,0);return;}
        const b={x:a.x-parent.x,y:a.y-parent.y,width:a.width,height:a.height},s=motion(clips,time,b,a.digits.length>0);
        if(identity(s)&&!clips.some(c=>time>=c.start&&(time<c.start+c.duration||holds(c.options.kind))))return;
        const composed=blank(r.id+"-soft-work",frame.width,frame.height),c=composed.getContext("2d");c.drawImage(frame,0,0);
        c.globalCompositeOperation="destination-out";c.drawImage(image(a.erase),b.x,b.y);c.globalCompositeOperation="source-over";c.drawImage(image(a.fill),b.x,b.y);
        c.save();transform(c,b,s);crop(c,b,s);c.globalAlpha=clamp(s.alpha);const number=blank(r.id+"-soft-number",a.width,a.height);if(digits(number.getContext("2d"),r,a,clips,time))c.drawImage(number,b.x,b.y);else c.drawImage(image(a.image),b.x,b.y);c.globalAlpha=1;c.translate(b.x,b.y);
        for(const clip of clips)if(time>=clip.start&&(time<clip.start+clip.duration||holds(clip.options.kind)))decoration(c,r,b.width,b.height,clip,progress(clip,time),s.alpha);
        c.restore();const masks=maskCoverage(r,parent,time,r.id+"-masks");multiply(composed,masks);
        const f=frame.getContext("2d");f.globalCompositeOperation="destination-out";f.drawImage(masks,0,0);f.globalCompositeOperation="lighter";f.drawImage(composed,0,0);f.globalCompositeOperation="source-over";
    }
    function render(time,rest=false) {
        if(rest&&plan.restMode!=="Hold"){ctx.clearRect(0,0,canvas.width,canvas.height);if(plan.restMode!=="Transparent")ctx.drawImage(image(data.source),0,0);return;}
        ctx.resetTransform(); ctx.globalAlpha = 1; ctx.globalCompositeOperation = "source-over"; ctx.clearRect(0, 0, canvas.width, canvas.height);
        ctx.drawImage(image(data.source), 0, 0);
        const roots = data.regions.filter(r => !r.parentId);
        const visible = r => r.enabled && (!r.parentId || data.regions.find(p => p.id === r.parentId)?.enabled);
        const plain = roots.every(r => r.enabled) && data.regions.filter(visible).every(r => {
            if (r.masks.some(m => m.enabled)) return false;
            const a = assets.get(r.id), clips = effective(r.clips, time), state = motion(clips, time, a, a.digits.length > 0);
            return identity(state) && !clips.some(c => time >= c.start && time < c.start+c.duration && ["DigitRoll", "Bounce"].includes(c.options.kind));
        });
        if(plain)for(const r of data.regions.filter(visible).slice().reverse()){const a=assets.get(r.id);ctx.save();ctx.translate(a.x,a.y);for(const c of effective(r.clips,time))if(time>=c.start&&(time<c.start+c.duration||holds(c.options.kind)))decoration(ctx,r,a.width,a.height,c,progress(c,time),1);ctx.restore();}
        if (!plain) {
            for (const r of roots) {
                if (r.overlayOnly || r.enabled && r.background === "KeepOriginal") continue;
                const a = assets.get(r.id); ctx.globalCompositeOperation = "destination-out"; ctx.drawImage(image(a.erase), a.x, a.y); ctx.globalCompositeOperation = "source-over"; ctx.drawImage(image(a.fill), a.x, a.y);
            }
            for (const r of roots.slice().reverse()) {
                if (!r.enabled) continue;
                const a = assets.get(r.id), clips = effective(r.clips, time), s = motion(clips, time, a, a.digits.length > 0);
                const frame = blank(r.id + "-frame", a.width, a.height), f = frame.getContext("2d"); f.drawImage(image(a.raw), 0, 0);
                if (r.role === "Group") for (const child of data.regions.filter(c => c.parentId === r.id && c.enabled).slice().reverse()) applyArea(frame, child, a, time);
                const number = blank(r.id + "-number", a.width, a.height);
                const numeric=digits(number.getContext("2d"), r, a, clips, time);
                if (numeric) { f.clearRect(0, 0, a.width, a.height); f.drawImage(number, 0, 0); }
                multiply(frame, image(numeric?(a.numericShape??a.shape):a.shape)); const masks = maskCoverage(r, a, time, r.id + "-masks"); multiply(frame, masks);
                ctx.save(); ctx.translate(a.x, a.y); transform(ctx, local(a), s);crop(ctx,local(a),s); ctx.globalAlpha = clamp(s.alpha); ctx.drawImage(frame, 0, 0); ctx.globalAlpha = 1;
                if (r.masks.some(m => m.enabled)) {
                    const decor = blank(r.id + "-decor", a.width, a.height), d = decor.getContext("2d");
                    for (const c of clips) if (time >= c.start && (time < c.start + c.duration || holds(c.options.kind))) decoration(d, r, a.width, a.height, c, progress(c, time), s.alpha);
                    multiply(decor, masks); ctx.drawImage(decor, 0, 0);
                } else for (const c of clips) if (time >= c.start && (time < c.start + c.duration || holds(c.options.kind))) decoration(ctx, r, a.width, a.height, c, progress(c, time), s.alpha);
                ctx.restore();
            }
        }
        if (plan.enabled && plan.exitFade && time >= B) {
            const alpha = plan.exitFrames <= 1 ? 0 : clamp((plan.endFrame - 1 - time * fps) / (plan.exitFrames - 1));
            ctx.globalCompositeOperation = "destination-in"; ctx.globalAlpha = alpha; ctx.fillStyle = "white"; ctx.fillRect(0, 0, canvas.width, canvas.height); ctx.globalCompositeOperation = "source-over"; ctx.globalAlpha = 1;
        }
    }
    let loaded = false, started = performance.now(), pausedAt = null, exitAt = null, lastFrame = -1, visible = true, manualTime = null, manualElapsed=null;
    const activeStorageKey="LiveCG.programActive:"+data.sourceName;
    function storedActive(){try{const v=window.sessionStorage?.getItem(activeStorageKey);return v==="true"?true:v==="false"?false:null;}catch{return null;}}
    // OBS sends activation edges, but a newly opened/reloaded page may not receive its current state.
    // Unknown state must autoplay; the next real program activation still restarts from zero.
    let obsHost=!!window.obsstudio,programActive=obsHost?storedActive():null,waitingForProgram=obsHost&&data.playback.replayOnShow&&programActive===false;
    const elapsed = () => waitingForProgram&&data.playback.replayOnShow?0:((pausedAt ?? performance.now()) - started) / 1000;
    const sample = e => {
        if (!plan.enabled) return { time: Math.min(e, END - 1 / fps), phase: e >= END ? "완료" : "전체 재생" };
        if (exitAt !== null && e >= exitAt) return { time: Math.min(END, B + e - exitAt), phase: e - exitAt >= END - B ? "퇴장 완료" : "퇴장" };
        const at=e<A?0:(e-A)%CYCLE, rest=e>=A&&REST>0&&at>=LOOP-1e-9;
        return { time: e < A ? e : Math.min(B-1/fps,A+at), rest, phase: e < A ? "등장" : rest?"쉬기":exitAt === null ? "반복" : "반복 끝 대기" };
    };
    const api = {
        restart() { waitingForProgram=false;started = performance.now(); pausedAt = null; exitAt = null; manualTime = null; manualElapsed=null;lastFrame = -1; },
        exit() { if(data.playback.exitEnabled===false)return; if (pausedAt !== null) api.resume(); const e = elapsed(); manualTime = null; if (exitAt === null) exitAt = data.playback.exitAtLoopEnd ? (e < A ? A + LOOP : sample(e).rest?e:A + Math.floor((e - A) / CYCLE + 1e-9)*CYCLE+LOOP) : e; },
        pause() { if (pausedAt === null) pausedAt = performance.now(); },
        resume() { if (pausedAt !== null) { started += performance.now() - pausedAt; pausedAt = null; } manualTime = null;manualElapsed=null; },
        togglePause() { pausedAt === null ? api.pause() : api.resume(); },
        seek(seconds) { api.pause(); manualElapsed=null;manualTime = clamp(Number(seconds) || 0, 0, END); lastFrame = -1; if (loaded) render(manualTime); },
        status() { const clock=manualElapsed??elapsed(),s = sample(clock); return { loaded, loopIndex:plan.enabled&&clock>=A?Math.floor((clock-A)/CYCLE):-1, ...s, ...(waitingForProgram&&data.playback.replayOnShow?{phase:"프로그램 대기"}:{}), ...(manualTime !== null ? { time: manualTime, phase: "프레임 확인" } : {}), paused: pausedAt !== null, exitRequested: exitAt !== null, visible,programActive,waitingForProgram, error: api.error ?? null }; },
        // Deterministic frame rendering is also used by browser/native regression checks.
        renderAt(seconds) { if (!loaded) throw new Error("이미지를 읽는 중입니다."); api.seek(seconds); return api.status(); },
        renderElapsedAt(seconds) { if(!loaded)throw new Error("이미지를 읽는 중입니다.");api.pause();manualTime=null;manualElapsed=Math.max(0,Number(seconds)||0);lastFrame=-1;const s=sample(manualElapsed);render(s.time,s.rest);return api.status(); }
    };
    window.LiveCG = api;
    window.addEventListener("keydown", e => { if (e.ctrlKey || e.altKey || e.metaKey) return; if (e.code === "KeyR") api.restart(); else if (e.code === "KeyE") api.exit(); else if (e.code === "Space") api.togglePause(); else return; e.preventDefault(); });
    window.addEventListener("obsSourceVisibleChanged", e => { if(typeof e.detail?.visible!=="boolean")return;const now=e.detail.visible;if(now===visible)return;visible=now;if(now&&data.playback.replayOnShow&&!obsHost)api.restart(); });
    window.addEventListener("obsSourceActiveChanged",e=>{if(typeof e.detail?.active!=="boolean")return;obsHost=true;const next=e.detail.active;try{window.sessionStorage?.setItem(activeStorageKey,String(next));}catch{}if(next===programActive)return;programActive=next;if(!data.playback.replayOnShow){waitingForProgram=false;return;}api.restart();waitingForProgram=!next;});
    window.addEventListener("LiveCGControl", e => { const v = e.detail ?? {}; if (v.target && v.target !== "all" && v.target !== data.sourceName) return; if (["restart", "exit", "pause", "resume"].includes(v.command)) api[v.command](); });
    const names = new Set([data.source]);
    for (const a of data.assets) { for (const key of ["raw", "shape", "numericShape", "image", "backing", "erase", "fill"]) if(a[key])names.add(a[key]); for(const glyph of a.rollGlyphs??[])names.add(glyph.image); for (const m of a.masks) names.add(m.image);for(const route of a.routes??[])for(const icon of Object.values(route.icons))names.add(icon); }
    Promise.all([...names].map(name => new Promise((resolve, reject) => { const img = new Image(); img.onload = () => { images.set(name, img); resolve(); }; img.onerror = () => reject(new Error("이미지 로드 실패: " + name)); img.src = name; }))).then(() => {
        loaded = true; api.restart();waitingForProgram=obsHost&&data.playback.replayOnShow&&programActive===false; const credit = document.getElementById("credit"); if (credit) credit.textContent = data.credit;
        document.title = "LiveCG-EFF · " + data.credit;
        function tick() {
            try {
                const s = sample(manualElapsed??elapsed()), time = manualTime ?? s.time, frame = Math.floor(time * fps + 1e-7), rest=manualTime===null&&s.rest;
                const key=frame+(rest?":rest":"");if (key !== lastFrame) { render(frame / fps,rest); lastFrame = key; }
                const label = document.getElementById("status"); if (label) label.textContent = `${s.phase} · ${time.toFixed(2)}초`;
                requestAnimationFrame(tick);
            } catch (error) { fail(error); }
        }
        tick();
    }).catch(fail);
    function fail(error) { api.error = String(error); const el = document.getElementById("error"); el.textContent = "LiveCG 오류: " + error.message; el.style.display = "block"; console.error(error); }
})();
