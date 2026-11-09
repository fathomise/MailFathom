// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The client's own scripts, which `MessageMarkupFrame.tsx` prepends to a frame's `srcDoc` ahead of the message markup.
// Each is exported as the text between its `<script>` tags and nothing else, because that exact text is what a content
// security policy admits by hash: a `srcdoc` document inherits the policy of the page embedding it, so
// `contentSecurityPolicy.ts` beside `vite.config.ts` hashes these strings, and a script edited here is admitted by
// the next build without anybody copying a digest. A character added between the tags anywhere else would be a script
// the policy refuses without a visible error.

// Reports a followed link. It cancels the frame's own handling of the click and reports the `href` **as the sender wrote
// it** — the attribute rather than the resolved property, because a relative reference resolves against `about:srcdoc`
// and would arrive as an address that means nothing. Whether the target is one a reader may be handed is the parent's
// decision and is taken there.
//
// The listener is on the document in the capture phase, so a link wrapped in whatever a template put around it is still
// answered by the first handler to see the event. It reads the tree upwards rather than trusting the event target,
// since a click lands on the text node's element — a `span` inside the anchor, the image inside a banner link.
//
// Beside the `href` it reports what the anchor showed — its words, and whether a picture stood in for them — because
// the question asked before the link opens says both. A middle press arrives as `auxclick` rather than `click` and is
// reported the same way; any other button is left alone.
export const linkScript = `(function(){
function anchor(n){while(n&&n.nodeType===1){if(n.nodeName==="A")return n;n=n.parentNode}return null}
function report(e){if(e.type==="auxclick"&&e.button!==1)return;var a=anchor(e.target);if(!a)return;
var href=a.getAttribute("href");if(!href)return;e.preventDefault();
try{parent.postMessage({link:href,words:a.textContent,picture:!!a.querySelector("img,svg,picture")},"*")}catch(err){}}
document.addEventListener("click",report,true);document.addEventListener("auxclick",report,true)})()`;

// Measures the document at a viewport height of zero — otherwise each fitting would enlarge the content it is measuring
// and the number would grow without end — and observes the body rather than the document element, which would close
// the same loop. It reports by `postMessage` and does nothing else, which is what makes granting the flag bounded.
//
// It also stops the framed document scrolling inside itself, which is the design's `scrolling="no"` written
// the way the platform still has: that attribute is deprecated and the lint set refuses it, and what replaces it is
// `overflow: hidden` on the framed document. The one case the frame is meant to scroll is the one where no report ever
// arrives, and nothing there ran this script to hide it.
export const measuringScript = `(function(){var last=0,sends=0;
function measure(){var de=document.documentElement,b=document.body;if(!de||!b)return 0;
var held=de.style.height;de.style.height="0px";
var h=Math.max(b.scrollHeight,b.offsetHeight,Math.ceil(b.getBoundingClientRect().height));
de.style.height=held;return h}
function send(){if(sends>24)return;var h=measure();
if(h&&Math.abs(h-last)>3){last=h;sends++;try{parent.postMessage({height:h},"*")}catch(e){}}}
function boot(){var de=document.documentElement,b=document.body;
if(de)de.style.overflow="hidden";if(b)b.style.overflow="hidden";
if(window.ResizeObserver&&b){try{new ResizeObserver(function(){send()}).observe(b)}catch(e){}}send()}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();
window.addEventListener("load",send);setTimeout(send,120);setTimeout(send,600);})()`;

// Moves a message written against a white page onto the theme's surface, which the frame shows through itself. The theme
// arrives as the `color-scheme` of the style ahead of this script, read before the sender's own rules are parsed. The
// frame holds an opaque origin, so nothing outside it can read a computed style and this is the one place the sender's
// colours can be seen: each element's fill, ink, and rules are read as the browser resolved them, and written back
// inline and `important`, which is what outranks whatever the sender's own rules said.
//
// A near-white fill goes altogether, so the theme's own surface stands behind it, and ink that would not reach WCAG's
// 4.5:1 on what is now behind it is moved until it does. Most mail sets no background and needs nothing else, which is
// the whole of the light theme; under the dark one a remaining light fill keeps only a hint of its hue, an achromatic
// ink simply takes the theme's own, and a light rule stops glaring. What decides is the ground each element stands on,
// walked down the tree: a fill the sender made dark or saturated keeps its colour and becomes the ground for its text,
// and an element painted with a picture or a gradient hands down no ground at all, because its text was paired with
// something only the sender saw. A colour written in a modern syntax computes to something other than `rgb()` and is
// left as written.
//
// `canvas` is a luminance rather than a colour, and it holds for every surface of its theme a frame is drawn on: the
// frame is transparent and cannot read the surface behind it, and those surfaces all sit within a rounding of this
// number for a contrast sum. `CanvasText` is the theme's ink only as nearly as the platform's own dark furniture is.
//
// shortcut: a transparent picture is left alone, so a dark logo on a transparent ground stays dark on a dark page; reading
// its pixels to plate it is the upgrade if mail is found to depend on it.
export const themeColoursScript = `(function(){var dark=getComputedStyle(document.documentElement).colorScheme==="dark",canvas=dark?0.02:0.95,lightFill=0.45,legible=4.5,hairline="color-mix(in srgb,CanvasText 16%,transparent)";
function parsed(v){var m=/^rgba?\\(([\\d.]+), ([\\d.]+), ([\\d.]+)(?:, ([\\d.]+))?\\)$/.exec(v);return m?[+m[1],+m[2],+m[3],m[4]===void 0?1:+m[4]]:null}
function linear(c){c/=255;return c<=0.03928?c/12.92:Math.pow((c+0.055)/1.055,2.4)}
function luminance(c){return 0.2126*linear(c[0])+0.7152*linear(c[1])+0.0722*linear(c[2])}
function contrast(a,b){return (Math.max(a,b)+0.05)/(Math.min(a,b)+0.05)}
function readable(c,ground){var target=ground>0.18?0:255;
if(dark&&target&&Math.max(c[0],c[1],c[2])-Math.min(c[0],c[1],c[2])<32)return "CanvasText";
for(var p=0.2;p<1;p+=0.2){var m=[0,1,2].map(function(i){return Math.round(c[i]+(target-c[i])*p)});if(contrast(luminance(m),ground)>=legible)return "rgb("+m+")"}
return "rgb("+target+","+target+","+target+")"}
function tint(c){var k=Math.round((255-Math.min(c[0],c[1],c[2]))/255*40);return k<4?"transparent":"color-mix(in srgb,rgb("+c.slice(0,3)+") "+k+"%,transparent)"}
function adapt(el,ground){var cs=getComputedStyle(el),s=el.style,fill=parsed(cs.backgroundColor);
if(cs.backgroundImage!=="none")ground=null;
else if(fill&&fill[3]>0){var l=luminance(fill),t=l>lightFill?tint(fill):null;if(t&&(dark||t==="transparent")){s.setProperty("background-color",t,"important");ground=canvas}else if(fill[3]===1)ground=l}
var ink=parsed(cs.color);if(ink&&ink[3]>0&&ground!==null&&contrast(luminance(ink),ground)<legible)s.setProperty("color",readable(ink,ground),"important");
["top","right","bottom","left"].forEach(function(side){var edge=parsed(cs.getPropertyValue("border-"+side+"-color"));
if(dark&&edge&&edge[3]>0&&ground!==null&&parseFloat(cs.getPropertyValue("border-"+side+"-width"))>0&&(luminance(edge)>lightFill||contrast(luminance(edge),ground)<1.25))s.setProperty("border-"+side+"-color",hairline,"important")});
for(var c=el.firstElementChild;c;c=c.nextElementSibling)adapt(c,ground)}
function run(){document.documentElement.style.setProperty("color-scheme",dark?"dark":"light","important");if(document.body)adapt(document.body,canvas)}
if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",run);else run()})()`;
