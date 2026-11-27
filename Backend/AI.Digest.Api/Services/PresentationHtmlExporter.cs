using System.Text.Encodings.Web;
using System.Text.Json;
using AI.Digest.Api.Models;

namespace AI.Digest.Api.Services;

public static class PresentationHtmlExporter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Default
    };

    public static string Export(PresentationDocument presentation)
    {
        var exportData = new
        {
            presentation.Title,
            presentation.Theme,
            ThemeKey = GetThemeKey(presentation.Theme),
            Slides = presentation.Slides.Select(slide => new
            {
                slide.SlideNumber,
                slide.SectionId,
                slide.Title,
                slide.Subtitle,
                slide.LayoutType,
                slide.Content,
                slide.VisualElements,
                slide.Diagram,
                slide.Chart,
                slide.SpeakerNotes,
                slide.SourceReferences
            })
        };
        var json = JsonSerializer.Serialize(exportData, JsonOptions);

        return """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <meta name="referrer" content="no-referrer">
              <meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; font-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'">
              <title>Interactive Presentation</title>
              <style>
                :root{color-scheme:light;--bg:#f4f7fc;--fg:#142b52;--muted:#64748b;--accent:#ffc928;--panel:rgba(255,255,255,.82);--line:rgba(100,116,139,.2)}
                *{box-sizing:border-box}html,body{margin:0;min-height:100%;font-family:Inter,"Segoe UI",Arial,sans-serif;background:var(--bg);color:var(--fg)}
                body{background:radial-gradient(circle at 85% 12%,rgba(255,201,40,.16),transparent 25%),linear-gradient(135deg,#f7f9fd,#edf3ff 60%,#fff)}
                body.dark{color-scheme:dark;--bg:#0b1730;--fg:#f8fafc;--muted:#c0ccdf;--panel:rgba(255,255,255,.08);--line:rgba(255,255,255,.17);background:radial-gradient(circle at 82% 18%,rgba(43,127,255,.24),transparent 30%),linear-gradient(135deg,#0b1730,#10264a)}
                body.theme-neural{color-scheme:dark;--bg:#070b18;--fg:#f5f8ff;--muted:#c1d0e7;--panel:rgba(13,25,52,.76);--line:rgba(105,220,255,.22);--accent:#24d8f2;background:radial-gradient(circle at 80% 16%,rgba(129,75,255,.3),transparent 32%),radial-gradient(circle at 16% 78%,rgba(0,222,255,.12),transparent 34%),linear-gradient(135deg,#050914,#0a1430)}
                body.theme-cinematic{color-scheme:dark;--bg:#15151b;--fg:#fffaf3;--muted:#d0c7bc;--panel:rgba(39,37,42,.78);--line:rgba(255,255,255,.14);--accent:#e7ad69;background:radial-gradient(circle at 82% 18%,rgba(255,176,77,.18),transparent 32%),linear-gradient(140deg,#111218,#1b1b24 55%,#33251e)}
                body.theme-gold{color-scheme:dark;--bg:#09152a;--fg:#fff9eb;--muted:#d1c9b8;--panel:rgba(255,255,255,.07);--line:rgba(226,196,125,.24);--accent:#e2c47d;background:radial-gradient(circle at 78% 16%,rgba(224,190,115,.2),transparent 28%),linear-gradient(135deg,#071326,#132442 64%,#202e46)}
                body.theme-blueprint{color-scheme:dark;--bg:#082243;--fg:#eef8ff;--muted:#bdd5e8;--panel:rgba(5,31,61,.8);--line:rgba(112,196,255,.28);--accent:#70c4ff;background:linear-gradient(rgba(99,174,255,.07) 1px,transparent 1px),linear-gradient(90deg,rgba(99,174,255,.07) 1px,transparent 1px),linear-gradient(135deg,#06162e,#0b2c52);background-size:34px 34px,34px 34px,auto}
                body.theme-story{color-scheme:light;--bg:#f4eadb;--fg:#2b2b30;--muted:#655a51;--panel:rgba(255,255,255,.67);--line:rgba(100,70,45,.18);--accent:#9c5d36;background:radial-gradient(circle at 86% 12%,rgba(237,174,91,.18),transparent 28%),linear-gradient(135deg,#fbf4e9,#f2e6d5 62%,#e7d7c3)}
                body.theme-universe{color-scheme:dark;--bg:#100d25;--fg:#f7f3ff;--muted:#d2c8e8;--panel:rgba(35,25,67,.76);--line:rgba(188,163,255,.22);--accent:#bca3ff;background:radial-gradient(circle at 70% 20%,rgba(134,81,255,.32),transparent 34%),radial-gradient(circle at 18% 82%,rgba(32,190,255,.12),transparent 35%),linear-gradient(135deg,#090817,#15122d 60%,#24143b)}
                .theme-neural .kicker{color:#65e8fa}.theme-cinematic .kicker{color:#ffbf78}.theme-gold .kicker{color:#e8ca85}.theme-blueprint .kicker{color:#8acbff}.theme-story .kicker{color:#9c5d36}.theme-universe .kicker{color:#bca3ff}
                button{font:inherit;color:inherit}#toolbar{position:fixed;z-index:5;inset:0 0 auto;min-height:58px;display:flex;align-items:center;justify-content:space-between;gap:12px;padding:10px 18px;border-bottom:1px solid var(--line);background:var(--panel);backdrop-filter:blur(14px)}
                #deck-title{max-width:28vw;overflow:hidden;font-size:13px;font-weight:750;text-overflow:ellipsis;white-space:nowrap}#controls{display:flex;align-items:center;justify-content:flex-end;gap:6px;flex-wrap:wrap}
                .control{min-height:34px;padding:0 10px;border:1px solid var(--line);border-radius:8px;background:var(--panel);cursor:pointer;font-size:11px}.control:hover,.control:focus-visible{outline:2px solid #477cf4;outline-offset:1px}
                #stage{min-height:100vh;padding:90px 8vw 92px;display:grid;place-items:center}.slide{position:relative;width:min(1120px,100%);min-height:min(72vh,720px);display:flex;flex-direction:column;justify-content:center;padding:clamp(28px,6vw,82px);overflow:hidden;border:1px solid var(--line);border-radius:22px;background:var(--panel);box-shadow:0 28px 80px rgba(20,43,82,.12);animation:enter .35s ease both}
                .slide:before{content:"";position:absolute;inset:0 auto 0 0;width:8px;background:linear-gradient(var(--fg),var(--accent))}.slide:after{content:"";position:absolute;z-index:0;width:36vw;height:36vw;right:-20vw;top:-25vw;border:1px solid var(--line);border-radius:50%;box-shadow:0 0 0 44px rgba(37,99,235,.025),0 0 0 88px rgba(255,201,40,.035);pointer-events:none}
                .slide.hero h1{max-width:1000px;font-size:clamp(48px,8vw,104px)}.slide.hero .subtitle{font-size:clamp(19px,2.4vw,30px)}.slide.workflow #points,.slide.blueprint #points{grid-template-columns:1fr}.slide.conclusion{border-color:var(--accent);box-shadow:0 28px 90px rgba(0,0,0,.2)}
                .sources{margin-top:16px;color:var(--muted);font-size:10px;line-height:1.55}.sources strong{display:block;margin-bottom:4px;color:var(--fg)}
                .slide>*{position:relative;z-index:1}.kicker{margin:0 0 18px;color:#2563eb;font-size:11px;font-weight:800;letter-spacing:.13em;text-transform:uppercase}.dark .kicker{color:#8dbbff}
                h1{max-width:950px;margin:0;font-size:clamp(36px,6.2vw,78px);line-height:1.02;letter-spacing:-.045em}h2{font-size:clamp(21px,3vw,34px)}.subtitle{max-width:780px;margin:18px 0 28px;color:var(--muted);font-size:clamp(16px,2vw,23px);line-height:1.5}
                #points{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:13px 26px;max-width:920px;margin-top:22px}.point{display:flex;align-items:flex-start;gap:12px;padding:12px 0;color:var(--fg);font-size:clamp(14px,1.5vw,19px);line-height:1.5;opacity:0;transform:translateY(8px);transition:opacity .32s ease,transform .32s ease}.point.visible{opacity:1;transform:none}.point:before{content:"";flex:0 0 9px;width:9px;height:9px;margin-top:8px;border-radius:50%;background:var(--accent);box-shadow:0 0 0 5px rgba(255,201,40,.15)}
                #visuals{display:grid;grid-template-columns:repeat(auto-fit,minmax(145px,1fr));gap:10px;margin-top:26px}.visual{padding:15px;border:1px solid var(--line);border-radius:13px;background:var(--panel)}.visual small{display:block;color:var(--muted);font-size:10px}.visual strong{display:block;margin-top:7px;font-size:20px;overflow-wrap:anywhere}
                #diagram,#chart{margin-top:22px;padding:18px;border:1px solid var(--line);border-radius:14px;background:var(--panel)}#diagram-title,#chart-title{font-size:12px;font-weight:750}.flow{display:flex;align-items:center;gap:10px;overflow:auto;padding:16px 2px}.node{flex:0 0 auto;max-width:220px;padding:12px 16px;border:1px solid var(--line);border-radius:12px;background:var(--panel);font-size:12px;text-align:center}.arrow{color:#2563eb;font-size:22px}.chart-row{display:grid;grid-template-columns:minmax(70px,150px) minmax(40px,1fr) 42px;align-items:center;gap:8px;margin:9px 0;font-size:11px}.chart-bar{height:13px;border-radius:9px;background:linear-gradient(90deg,#2563eb,var(--accent));min-width:2px}
                #footer{position:fixed;z-index:4;inset:auto 0 0;display:flex;align-items:center;justify-content:center;gap:10px;min-height:64px;padding:10px 16px;background:var(--panel);border-top:1px solid var(--line);backdrop-filter:blur(14px)}#progress{width:min(420px,35vw);height:4px;overflow:hidden;border-radius:9px;background:var(--line)}#progress i{display:block;width:0;height:100%;background:var(--accent);transition:width .25s ease}#position{min-width:58px;text-align:center;color:var(--muted);font-size:11px}
                #overlay{position:fixed;z-index:8;inset:72px 18px 76px auto;width:min(520px,calc(100vw - 36px));padding:16px;overflow:auto;border:1px solid var(--line);border-radius:16px;background:var(--bg);box-shadow:0 20px 70px rgba(0,0,0,.2)}#overlay[hidden]{display:none}.overview-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:8px}.overview-item{padding:12px;border:1px solid var(--line);border-radius:10px;background:var(--panel);text-align:left;cursor:pointer}.overview-item strong{display:block;margin-top:5px;font-size:12px}.notes{color:var(--muted);font-size:14px;line-height:1.7;white-space:pre-wrap}
                body.scroll-mode{scroll-snap-type:y mandatory}body.scroll-mode #stage{display:block;padding:58px 8vw 0}body.scroll-mode .slide{width:min(1120px,100%);min-height:calc(100vh - 122px);margin:26px auto;scroll-snap-align:start}
                body.presentation #toolbar{opacity:.08;transition:opacity .2s}body.presentation #toolbar:hover,body.presentation #toolbar:focus-within{opacity:1}
                @keyframes enter{from{opacity:0;transform:translateY(14px)}to{opacity:1;transform:none}}
                @media(max-width:700px){#toolbar{align-items:flex-start;flex-direction:column;padding:8px 12px}#deck-title{max-width:90vw}#controls{justify-content:flex-start}.control{min-height:30px;padding:0 8px;font-size:10px}#stage{padding:138px 14px 82px}.slide{min-height:calc(100vh - 225px);padding:30px 24px}#points{grid-template-columns:1fr;gap:4px}#footer{min-height:54px}.timer-label{display:none}.slide:after{width:80vw;height:80vw}}
                @media(prefers-reduced-motion:reduce){*,*:before,*:after{scroll-behavior:auto!important;animation-duration:.01ms!important;transition-duration:.01ms!important}}
              </style>
            </head>
            <body>
              <header id="toolbar">
                <div id="deck-title"></div>
                <nav id="controls" aria-label="Presentation controls">
                  <button class="control" id="start">Start</button><button class="control" id="prev" aria-label="Previous">←</button><button class="control" id="next" aria-label="Next">Next →</button><button class="control" id="fullscreen">Full screen</button><button class="control" id="overview-toggle">Overview</button><button class="control" id="notes-toggle">Notes</button><button class="control" id="timer-toggle">Timer</button><button class="control" id="theme">Theme</button><button class="control" id="mode">Scroll mode</button><button class="control" id="help">Help</button><button class="control" id="restart">Restart</button>
                </nav>
              </header>
              <main id="stage" aria-live="polite"></main>
              <footer id="footer"><button class="control" id="footer-prev" aria-label="Previous section">←</button><span id="position"></span><div id="progress"><i></i></div><span id="timer-label" class="timer-label">0:00</span><button class="control" id="footer-next" aria-label="Next section">→</button></footer>
              <aside id="overlay" hidden></aside>
              <script id="presentation-data" type="application/json">__PRESENTATION_DATA_JSON__</script>
              <script>
              (()=>{"use strict";
                const data=JSON.parse(document.getElementById("presentation-data").textContent);
                const slides=Array.isArray(data.slides)?data.slides:[];
                const stage=document.getElementById("stage"),overlay=document.getElementById("overlay");
                const title=document.getElementById("deck-title"),pos=document.getElementById("position"),bar=document.querySelector("#progress i"),clock=document.getElementById("timer-label");
                title.textContent=data.title||"Interactive presentation";document.title=title.textContent;
                const themeKeys=["neural","cinematic","gold","blueprint","story","universe","default"];
                let activeTheme=themeKeys.includes(data.themeKey)?data.themeKey:"default";
                let index=0,revealed=0,seconds=0,timer=null,dark=(data.theme||"").toLowerCase().includes("dark");
                document.body.classList.add("theme-"+activeTheme);
                if(dark)document.body.classList.add("dark");
                const el=(tag,cls,text)=>{const n=document.createElement(tag);if(cls)n.className=cls;if(text!==undefined)n.textContent=String(text);return n};
                const addText=(parent,tag,cls,text)=>{const n=el(tag,cls,text);parent.appendChild(n);return n};
                const displayTime=n=>`${Math.floor(n/60)}:${String(n%60).padStart(2,"0")}`;
                function createSlide(s,i,revealCount){
                  const article=el("article","slide");
                  const layouts={title:"hero",hero:"hero",executivesummary:"hero",workflow:"workflow",architecturediagram:"blueprint",blueprint:"blueprint",conclusion:"conclusion"};
                  article.classList.add(layouts[String(s.layoutType||"").toLowerCase()]||"story");
                  addText(article,"p","kicker",`${s.layoutType||"Story"} · ${String(i+1).padStart(2,"0")}`);
                  addText(article,"h1","",s.title||"Untitled section");
                  if(s.subtitle)addText(article,"p","subtitle",s.subtitle);
                  const points=el("div","");points.id="points";
                  (Array.isArray(s.content)?s.content:[]).forEach((value,j)=>{const p=addText(points,"p","point",value);if(j<revealCount)p.classList.add("visible")});
                  article.appendChild(points);
                  if(Array.isArray(s.visualElements)&&s.visualElements.length){const visual=el("div","");visual.id="visuals";s.visualElements.forEach(item=>{const box=el("div","visual");addText(box,"small","",item.label||"");addText(box,"strong","",item.value||"");visual.appendChild(box)});article.appendChild(visual)}
                  if(s.diagram&&typeof s.diagram.mermaid==="string")renderDiagram(article,s.diagram);
                  if(s.chart&&Array.isArray(s.chart.labels)&&Array.isArray(s.chart.values))renderChart(article,s.chart);
                  if(Array.isArray(s.sourceReferences)&&s.sourceReferences.length){const refs=el("div","sources");addText(refs,"strong","","Source references");s.sourceReferences.slice(0,10).forEach(ref=>addText(refs,"span","",ref));article.appendChild(refs)}
                  addText(article,"small","","Section "+(i+1)+" of "+slides.length);
                  return article
                }
                function render(){
                  stage.replaceChildren();overlay.hidden=true;
                  if(!slides.length){stage.appendChild(el("p","", "This presentation has no sections."));return}
                  if(document.body.classList.contains("scroll-mode")){
                    slides.forEach((s,i)=>stage.appendChild(createSlide(s,i,(s.content||[]).length)));
                    stage.querySelectorAll(".slide")[index]?.scrollIntoView({block:"start",behavior:"smooth"});
                  }else stage.appendChild(createSlide(slides[index]||{},index,revealed));
                  pos.textContent=`${index+1} / ${slides.length}`;
                  bar.style.width=`${((index+1)/slides.length)*100}%`;
                }
                function renderDiagram(parent,diagram){
                  const box=el("section","");box.id="diagram";addText(box,"div","","Architecture / workflow");
                  const flow=el("div","flow"),source=diagram.mermaid.split(/\r?\n/).slice(1);
                  const labels=[],seen=new Set();
                  for(const line of source){const re=/([A-Za-z0-9_-]+)\s*\[([^\]]+)\]/g;let match;while((match=re.exec(line))){if(!seen.has(match[1])){seen.add(match[1]);labels.push(match[2])}}}
                  if(labels.length>0&&labels.length<=16){labels.forEach((label,i)=>{if(i)flow.appendChild(el("span","arrow","→"));flow.appendChild(el("div","node",label))})}
                  else flow.appendChild(el("div","node",diagram.type||"Diagram"));
                  box.appendChild(flow);const details=document.createElement("details");addText(details,"summary","","View diagram source");addText(details,"pre","",diagram.mermaid);box.appendChild(details);parent.appendChild(box)
                }
                function renderChart(parent,chart){
                  const rows=chart.labels.map((label,i)=>({label,value:Number(chart.values[i])})).filter(x=>Number.isFinite(x.value));
                  if(!rows.length||rows.length>20)return;
                  const box=el("section","");box.id="chart";addText(box,"div","","Source-provided chart · "+(chart.type||"Data"));
                  const max=Math.max(1,...rows.map(x=>Math.max(0,x.value)));
                  rows.forEach(row=>{const line=el("div","chart-row");addText(line,"span","",row.label);const bar=el("div","chart-bar");bar.style.width=`${Math.max(2,Math.max(0,row.value)/max*100)}%`;line.appendChild(bar);addText(line,"strong","",row.value);box.appendChild(line)});
                  parent.appendChild(box)
                }
                function next(){if(!document.body.classList.contains("scroll-mode")&&revealed<(slides[index]?.content||[]).length){revealed++;render();return}if(index<slides.length-1){index++;revealed=0;render()}}
                function previous(){if(!document.body.classList.contains("scroll-mode")&&revealed>0){revealed--;render();return}if(index>0){index--;revealed=(slides[index]?.content||[]).length;render()}}
                function showOverview(){overlay.replaceChildren();addText(overlay,"h2","","Presentation overview");const grid=el("div","overview-grid");slides.forEach((s,i)=>{const b=el("button","overview-item");b.appendChild(el("span","",String(i+1)));b.appendChild(el("strong","",s.title||"Untitled"));b.addEventListener("click",()=>{index=i;revealed=0;render()});grid.appendChild(b)});overlay.appendChild(grid);overlay.hidden=false}
                function showNotes(){overlay.replaceChildren();addText(overlay,"h2","","Presenter notes");addText(overlay,"p","notes",slides[index]?.speakerNotes||"No notes for this section.");overlay.hidden=false}
                document.getElementById("prev").onclick=document.getElementById("footer-prev").onclick=previous;
                document.getElementById("next").onclick=document.getElementById("footer-next").onclick=next;
                document.getElementById("start").onclick=()=>{document.body.classList.add("presentation");document.documentElement.requestFullscreen?.().catch(()=>{})};
                document.getElementById("fullscreen").onclick=()=>document.fullscreenElement?document.exitFullscreen?.():document.documentElement.requestFullscreen?.().catch(()=>{});
                document.getElementById("overview-toggle").onclick=showOverview;document.getElementById("notes-toggle").onclick=showNotes;
                document.getElementById("timer-toggle").onclick=e=>{if(timer){clearInterval(timer);timer=null;e.currentTarget.textContent="Timer"}else{timer=setInterval(()=>{seconds++;clock.textContent=displayTime(seconds)},1000);e.currentTarget.textContent="Pause timer"}};
                document.getElementById("theme").onclick=()=>{document.body.classList.remove(...themeKeys.map(key=>"theme-"+key),"dark");activeTheme=themeKeys[(themeKeys.indexOf(activeTheme)+1)%themeKeys.length];document.body.classList.add("theme-"+activeTheme)};
                document.getElementById("mode").onclick=e=>{const on=document.body.classList.toggle("scroll-mode");e.currentTarget.textContent=on?"Linear mode":"Scroll mode";revealed=on?(slides[index]?.content||[]).length:0;render()};
                document.getElementById("help").onclick=()=>{overlay.replaceChildren();addText(overlay,"h2","","Keyboard help");addText(overlay,"p","notes","Right/Space: reveal or next · Left: previous · F: full screen · O: overview · N: notes · Home/End: first/last · Escape: close panels");overlay.hidden=false};
                document.getElementById("restart").onclick=()=>{index=0;revealed=0;seconds=0;clock.textContent="0:00";render()};
                document.addEventListener("keydown",e=>{if(e.target instanceof HTMLElement&&/^(INPUT|TEXTAREA|SELECT)$/.test(e.target.tagName))return;
                  if(e.key==="ArrowRight"||e.key===" "){e.preventDefault();next()}else if(e.key==="ArrowLeft"){e.preventDefault();previous()}else if(e.key.toLowerCase()==="f"){document.getElementById("fullscreen").click()}else if(e.key.toLowerCase()==="o"){showOverview()}else if(e.key.toLowerCase()==="n"){showNotes()}else if(e.key==="Home"){index=0;revealed=0;render()}else if(e.key==="End"){index=slides.length-1;revealed=(slides[index]?.content||[]).length;render()}else if(e.key==="Escape"){if(!overlay.hidden)overlay.hidden=true;else if(document.fullscreenElement)document.exitFullscreen?.();else document.body.classList.remove("presentation")}
                });
                document.addEventListener("scroll",()=>{if(!document.body.classList.contains("scroll-mode"))return;const cards=[...stage.querySelectorAll(".slide")];let best=0,distance=Infinity;cards.forEach((card,i)=>{const d=Math.abs(card.getBoundingClientRect().top-80);if(d<distance){distance=d;best=i}});if(best!==index){index=best;revealed=(slides[index]?.content||[]).length;pos.textContent=`${index+1} / ${slides.length}`;bar.style.width=`${((index+1)/slides.length)*100}%`}},{passive:true});
                render();
              })();
              </script>
            </body>
            </html>
            """.Replace("__PRESENTATION_DATA_JSON__", json, StringComparison.Ordinal);
    }

    public static string SafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new string(title.Where(character => !invalid.Contains(character)).ToArray());
        name = string.Join("-", name.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(name) ? "interactive-presentation.html" : $"{name[..Math.Min(name.Length, 80)]}.html";
    }

    private static string GetThemeKey(string? theme)
    {
        var value = (theme ?? "").ToLowerInvariant();
        if (value.Contains("neural") || value.Contains("dark")) return "neural";
        if (value.Contains("cinematic")) return "cinematic";
        if (value.Contains("gold")) return "gold";
        if (value.Contains("blueprint") || value.Contains("technology")) return "blueprint";
        if (value.Contains("universe")) return "universe";
        if (value.Contains("storytelling")) return "story";
        return "default";
    }
}
