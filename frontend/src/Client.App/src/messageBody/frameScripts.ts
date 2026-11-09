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
// A near-white fill goes altogether, so what stood behind it shows, and ink that would not reach WCAG's
// 4.5:1 on what is now behind it is moved until it does. Most mail sets no background and needs nothing else, which is
// the whole of the light theme; under the dark one a remaining light fill keeps only a hint of its hue, an achromatic
// ink simply takes the theme's own, and a light rule stops glaring. What decides is the ground each element stands on,
// walked down the tree: a fill the sender made dark or saturated keeps its colour and becomes the ground for its text,
// and an element painted with a picture or a gradient hands down no ground at all, because its text was paired with
// something only the sender saw. A colour written in a modern syntax computes to something other than `rgb()` and is
// left as written.
//
// `surfaceChannel` is a grey standing for every surface of its theme a frame is drawn on: the frame is transparent and
// cannot read the surface behind it, and those surfaces all sit within a rounding of this grey for a contrast sum. A
// tinted fill is judged as what it paints, the ground behind it with the fill's share mixed in. `CanvasText` is the
// theme's ink only as nearly as the platform's own dark furniture is.
//
// shortcut: a transparent picture is left alone, so a dark logo on a transparent ground stays dark on a dark page; reading
// its pixels to plate it is the upgrade if mail is found to depend on it.
export const themeColoursScript = `(() => {
    const dark = getComputedStyle(document.documentElement).colorScheme === 'dark';
    const legibleContrast = 4.5;
    const lightFillLuminance = 0.45;
    const groundTakingDarkInk = 0.18;
    const faintestRuleContrast = 1.25;
    const mostTintPercent = 40;
    const leastTintPercent = 4;
    const hairline = 'color-mix(in srgb, CanvasText 16%, transparent)';
    const surfaceChannel = dark ? 39 : 248;
    const surface = [surfaceChannel, surfaceChannel, surfaceChannel];

    function colourOf(computedValue) {
        if (!computedValue.startsWith('rgb')) {
            return null;
        }

        const [red, green, blue, alpha = 1] = computedValue.match(/[0-9.]+/g).map(Number);

        return { channels: [red, green, blue], alpha };
    }

    function linearChannel(channel) {
        const share = channel / 255;

        return share <= 0.03928 ? share / 12.92 : ((share + 0.055) / 1.055) ** 2.4;
    }

    function luminanceOf([red, green, blue]) {
        return 0.2126 * linearChannel(red) + 0.7152 * linearChannel(green) + 0.0722 * linearChannel(blue);
    }

    function contrastBetween(first, second) {
        return (Math.max(first, second) + 0.05) / (Math.min(first, second) + 0.05);
    }

    function mixed(from, to, share) {
        return from.map((channel, index) => Math.round(channel + (to[index] - channel) * share));
    }

    function rgb(channels) {
        return 'rgb(' + channels.join(',') + ')';
    }

    function readableInk(channels, ground) {
        const lifting = ground <= groundTakingDarkInk;
        const destination = lifting ? [255, 255, 255] : [0, 0, 0];
        const achromatic = Math.max(...channels) - Math.min(...channels) < 32;

        if (dark && lifting && achromatic) {
            return 'CanvasText';
        }

        for (let share = 0.2; share < 1; share += 0.2) {
            const moved = mixed(channels, destination, share);

            if (contrastBetween(luminanceOf(moved), ground) >= legibleContrast) {
                return rgb(moved);
            }
        }

        return rgb(destination);
    }

    function groundAfterFill(style, computed, inheritedGround) {
        if (computed.backgroundImage !== 'none') {
            return null;
        }

        const fill = colourOf(computed.backgroundColor);

        if (fill === null || fill.alpha === 0) {
            return inheritedGround;
        }

        const fillLuminance = luminanceOf(fill.channels);
        const fillAsWritten = fill.alpha === 1 ? fill.channels : inheritedGround;

        if (fillLuminance <= lightFillLuminance) {
            return fillAsWritten;
        }

        const tintPercent = Math.round(((255 - Math.min(...fill.channels)) / 255) * mostTintPercent);

        if (tintPercent < leastTintPercent) {
            style.setProperty('background-color', 'transparent', 'important');

            return inheritedGround;
        }

        if (!dark) {
            return fillAsWritten;
        }

        style.setProperty(
            'background-color',
            'color-mix(in srgb, ' + rgb(fill.channels) + ' ' + tintPercent + '%, transparent)',
            'important',
        );

        return inheritedGround === null ? null : mixed(inheritedGround, fill.channels, tintPercent / 100);
    }

    function makeInkReadable(style, computed, ground) {
        const ink = colourOf(computed.color);

        if (ink === null || ink.alpha === 0 || ground === null) {
            return;
        }

        const groundLuminance = luminanceOf(ground);

        if (contrastBetween(luminanceOf(ink.channels), groundLuminance) < legibleContrast) {
            style.setProperty('color', readableInk(ink.channels, groundLuminance), 'important');
        }
    }

    function softenRules(style, computed, ground) {
        if (!dark || ground === null) {
            return;
        }

        const groundLuminance = luminanceOf(ground);

        for (const side of ['top', 'right', 'bottom', 'left']) {
            const rule = colourOf(computed.getPropertyValue('border-' + side + '-color'));
            const width = parseFloat(computed.getPropertyValue('border-' + side + '-width'));

            if (rule === null || rule.alpha === 0 || !(width > 0)) {
                continue;
            }

            const ruleLuminance = luminanceOf(rule.channels);

            if (ruleLuminance > lightFillLuminance || contrastBetween(ruleLuminance, groundLuminance) < faintestRuleContrast) {
                style.setProperty('border-' + side + '-color', hairline, 'important');
            }
        }
    }

    function adapt(element, inheritedGround) {
        const computed = getComputedStyle(element);
        const ground = groundAfterFill(element.style, computed, inheritedGround);

        makeInkReadable(element.style, computed, ground);
        softenRules(element.style, computed, ground);

        for (const child of element.children) {
            adapt(child, ground);
        }
    }

    function moveTheMessageOntoTheTheme() {
        document.documentElement.style.setProperty('color-scheme', dark ? 'dark' : 'light', 'important');

        if (document.body !== null) {
            adapt(document.body, surface);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', moveTheMessageOntoTheTheme);
    } else {
        moveTheMessageOntoTheTheme();
    }
})()`;
