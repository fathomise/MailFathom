// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

MailFathomDesign.mailHtml = (() => {
  const { mdPlain } = MailFathomDesign.markdown;

  /* The original messages as their senders wrote them — what actually arrives from the server; the thread window
     shows cleaned-up text and the original opens on demand (the "code" icon) in a separate tab / preview. */
  const HTML_SHELL = (title, body) => '<!doctype html><html lang="en"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>' + title + '</title></head>' +
    '<body style="margin:0;padding:0;background:#eef1f5;font-family:Georgia,\'Times New Roman\',serif;color:#1f2933">' + body + '</body></html>';
  /* Measuring script injected BEFORE the message markup. It runs in an opaque origin and does
     exactly one thing: report height via parent.postMessage. The message markup itself carries
     no scripts - it is sanitised before it gets here. */
  const S_OPEN = "<" + "script>", S_CLOSE = "<" + "/script>";
  const FIT_SCRIPT = (frameId) => S_OPEN + '(function(){var ID=' + JSON.stringify(frameId) + ',last=0,sends=0;' +
    /* We measure with a zero-height viewport so the result does not depend on the frame height
       - otherwise every fit would grow the content and the measurement would never settle. */
    'function measure(){var de=document.documentElement,b=document.body;if(!de||!b)return 0;' +
    'var prev=de.style.height;de.style.height="0px";' +
    'var h=Math.max(b.scrollHeight,b.offsetHeight,Math.ceil(b.getBoundingClientRect().height));' +
    'de.style.height=prev;return h}' +
    'function send(){if(sends>24)return;var h=measure();' +
    'if(h&&Math.abs(h-last)>3){last=h;sends++;try{parent.postMessage({frameId:ID,height:h},"*")}catch(e){}}}' +
    'function boot(){var de=document.documentElement,b=document.body;' +
    'if(de)de.style.overflow="hidden";if(b)b.style.overflow="hidden";' +
    /* We observe the content (body), not the viewport — observing documentElement would close the loop. */
    'if(window.ResizeObserver&&b){try{new ResizeObserver(function(){send()}).observe(b)}catch(e){}}send()}' +
    'if(document.readyState==="loading")document.addEventListener("DOMContentLoaded",boot);else boot();' +
    'window.addEventListener("load",send);setTimeout(send,120);setTimeout(send,600);})()' + S_CLOSE;

  /* The frame follows no link itself: a press on one, or on a picture inside one, is handed to the page, which asks
     before anything leaves MailFathom. */
  const LINK_SCRIPT = S_OPEN + '(function(){function press(e){if(e.button>1)return;' +
    'var a=e.target&&e.target.closest?e.target.closest("a[href]"):null;if(!a)return;e.preventDefault();' +
    'try{parent.postMessage({link:{href:a.getAttribute("href"),text:(a.textContent||"").trim(),image:!!a.querySelector("img,svg,picture")}},"*")}catch(x){}}' +
    'document.addEventListener("click",press,true);document.addEventListener("auxclick",press,true)})()' + S_CLOSE;

  /* The page behind a message is the theme's own in both themes: the frame is transparent, the page shows through it,
     and a near-white fill goes with it. Text that would not read 4.5:1 on what is now behind it is moved until it does,
     and a fill the sender made dark or saturated stays as written. Under the dark theme the remaining light fills keep
     only a hint of their hue and a light rule stops glaring. Text over a picture or a gradient is left as the sender
     paired it. */
  const THEME = (dark) => "<" + "style>html,body{background:transparent!important}html{color-scheme:" + (dark ? "dark" : "light") + "!important}<" + "/style>" +
    S_OPEN + `(() => {
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
})()` + S_CLOSE;

  const inHead = (src, script) => {
    const i = src.indexOf("<head>");
    return i < 0 ? script + src : src.slice(0, i + 6) + script + src.slice(i + 6);
  };
  const embedHtml = (src, frameId, dark) => inHead(src, THEME(dark) + FIT_SCRIPT(frameId) + LINK_SCRIPT);
  const embedLinks = (src, dark) => inHead(src, THEME(dark) + LINK_SCRIPT);

  const htmlFallback = (m, subject) => HTML_SHELL(subject || "Message", `
<div style="padding:24px 12px;font-family:Georgia,'Times New Roman',serif">
 <table role="presentation" width="600" align="center" cellpadding="0" cellspacing="0" style="width:600px;max-width:100%;background:#ffffff;border:1px solid #dde3ea">
  <tr><td style="padding:20px 24px;border-bottom:1px solid #eef1f5;font-family:Arial,sans-serif">
   <div style="font-size:15px;color:#14293f"><strong>` + (subject || "Message") + `</strong></div>
   <div style="font-size:12px;color:#7b8794;margin-top:4px">` + m.from + ` · ` + m.when + `</div>
  </td></tr>
  <tr><td style="padding:22px 24px;font-size:15px;line-height:1.7;color:#2c3a47">`
     + m.paras.map(p => '<p style="margin:0 0 14px 0">' + (p.text || mdPlain(p.md || "")) + '</p>').join("") +
    `</td></tr>
  <tr><td style="background:#f7f9fb;border-top:1px solid #e3e8ee;padding:13px 24px;font-family:Arial,sans-serif;font-size:11px;color:#9aa5b1">HTML version of the message · MailFathom shows simplified text by default</td></tr>
 </table>
</div>`);

  return { HTML_SHELL, FIT_SCRIPT, embedHtml, embedLinks, htmlFallback };
})();
