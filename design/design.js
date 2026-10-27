// Copyright © 2026 Krzysztof Kasprowicz
// Licensed under the GNU Affero General Public License, Version 3. See LICENSE in the project root for license information.
// Project repository: https://github.com/fathomise/MailFathom

// The one global every artboard's files share. A data file adds its sample data under `data`, and an artboard's logic
// registers under `artboards` a factory the design runtime calls with its own `DCLogic` and `React` once both exist.
// Logic an artboard splits into modules of its own hangs each one here under its own name.
window.MailFathomDesign = { data: {}, artboards: {} };

// Escape takes an explaining chip's sentence away while the chip keeps focus, and leaving the chip resets that; the
// sentence itself is base.css's `[data-tip]`.
document.addEventListener('keydown', (event) => {
  const chip = document.activeElement;
  if (event.key === 'Escape' && chip && chip.matches && chip.matches('[data-tip]')) chip.setAttribute('data-tip-dismissed', '');
});
document.addEventListener('focusout', (event) => {
  if (event.target && event.target.removeAttribute) event.target.removeAttribute('data-tip-dismissed');
});

// A control's name, which every artboard writes as its `title`, drawn as the same bubble `[data-tip]` draws rather than
// as the browser's own title — which no touch browser shows. A mouse rests on it for 400 ms; a finger holds it for
// 420 ms, and the tap that hold ends in is swallowed so the control is not also pressed; a tap on a control that
// cannot act (`aria-disabled="true"`) shows why. One bubble at a time, gone at the next press anywhere, on scroll, on
// Escape, and 4 s after a finger brought it up. An element inside a row whose own hold opens a menu carries
// `data-tip-hover`: a mouse still reads it, and a finger reads it in that menu instead.
(() => {
  let bubble = null, owner = null, clock = 0, held = null, swallow = null, handing = false;
  const named = (el) => el && el.closest && el.closest('[title]:not(iframe), [data-tip-name]');
  const nameOf = (el) => {
    if (el.hasAttribute('title')) { el.setAttribute('data-tip-name', el.getAttribute('title')); el.removeAttribute('title'); }
    return el.getAttribute('data-tip-name');
  };
  const hide = () => { clearTimeout(clock); if (bubble) bubble.remove(); bubble = null; owner = null; };
  const show = (el, goesAfter) => {
    hide();
    const text = nameOf(el);
    if (!text) return;
    owner = el;
    bubble = document.createElement('div');
    bubble.className = 'mf-name-tip';
    bubble.setAttribute('role', 'tooltip');
    bubble.textContent = text;
    (el.closest('dialog') || document.body).appendChild(bubble);
    const r = el.getBoundingClientRect(), b = bubble.getBoundingClientRect();
    const y = r.bottom + 6 + b.height > innerHeight ? r.top - 6 - b.height : r.bottom + 6;
    bubble.style.left = Math.max(10, Math.min(r.left, innerWidth - b.width - 10)) + 'px';
    bubble.style.top = Math.max(10, y) + 'px';
    if (goesAfter) clock = setTimeout(hide, goesAfter);
  };
  document.addEventListener('pointerover', (e) => {
    const el = named(e.target);
    if (el) nameOf(el);
    if (e.pointerType !== 'mouse' || !el || el === owner) return;
    clearTimeout(clock);
    clock = setTimeout(() => show(el), 400);
  });
  document.addEventListener('pointerout', (e) => {
    if (e.pointerType !== 'mouse') return;
    const el = named(e.target);
    if (el && !el.contains(e.relatedTarget)) { if (el === owner) hide(); else clearTimeout(clock); }
  });
  document.addEventListener('pointerdown', (e) => {
    const el = named(e.target);
    if (owner && el !== owner) hide();
    if (e.pointerType === 'mouse') { if (el && el === owner) hide(); else clearTimeout(clock); return; }
    if (!el || el.hasAttribute('data-tip-hover')) return;
    // The hold is this element's, so the row around it never also opens its menu or starts a swipe.
    e.stopPropagation();
    held = { el, x: e.clientX, y: e.clientY };
    clearTimeout(clock);
    clock = setTimeout(() => {
      swallow = el;
      // An element marked `data-hold` does something on a hold as well: it is handed the hold as the menu gesture a
      // mouse's right-click is, and the bubble then reads the name that act left standing.
      if (el.hasAttribute('data-hold')) {
        handing = true;
        el.dispatchEvent(new MouseEvent('contextmenu', { bubbles: true, cancelable: true }));
        handing = false;
        setTimeout(() => show(el, 4000));
        return;
      }
      show(el, 4000);
    }, 420);
  }, true);
  document.addEventListener('touchstart', (e) => {
    const el = named(e.target);
    if (el && !el.hasAttribute('data-tip-hover')) e.stopPropagation();
  }, true);
  document.addEventListener('pointermove', (e) => {
    if (!held || Math.hypot(e.clientX - held.x, e.clientY - held.y) <= 10) return;
    held = null;
    if (swallow) { swallow = null; hide(); } else clearTimeout(clock);
  }, true);
  document.addEventListener('pointerup', () => { if (held && !swallow) clearTimeout(clock); held = null; }, true);
  document.addEventListener('contextmenu', (e) => { if (!handing && (held || swallow)) { e.preventDefault(); e.stopPropagation(); } }, true);
  document.addEventListener('click', (e) => {
    const el = named(e.target);
    if (swallow && el === swallow) { swallow = null; e.preventDefault(); e.stopPropagation(); return; }
    swallow = null;
    if (el && el.getAttribute('aria-disabled') === 'true' && !el.hasAttribute('data-tip-hover')) show(el, 4000);
  }, true);
  document.addEventListener('focusin', (e) => { const el = named(e.target); if (el && el.matches(':focus-visible')) show(el); });
  document.addEventListener('focusout', (e) => { if (e.target === owner) hide(); });
  document.addEventListener('keydown', (e) => { if (e.key === 'Escape' && bubble) hide(); });
  addEventListener('scroll', hide, true);
  addEventListener('resize', hide);
})();

// A toast dragged aside, the same in every artboard that stands one: the card follows the pointer across once it has
// travelled 14 px, fading as it goes and carried at most 148 px; let go past 96 px it leaves that way and `gone` takes
// it, short of that it springs back; more than 26 px up or down, and more down than across, is a scroll and springs it
// back too. A card that `asks` — a running operation's — springs back as `gone` asks its question. Every pointer drags, a mouse included, because the close control stays beside it. The pointer is captured
// only once the drag engages, so a press on the close control still lands on the close control, and `dragged` tells a
// card's own click that the press it ends was a drag rather than a press.
MailFathomDesign.toastDrag = (gone, asks) => {
  const back = (el, d) => {
    el.style.transition = "transform .22s var(--ease-sheet), opacity .22s ease";
    el.style.transform = "";
    el.style.opacity = d.opacity;
    setTimeout(() => { el.style.transition = d.transition; }, 230);
  };
  return {
    down: (e) => {
      if (e.button > 0) return;
      e.currentTarget._drag = { pointer: e.pointerId, x: e.clientX, y: e.clientY, on: false, dx: 0 };
    },
    move: (e) => {
      const el = e.currentTarget, d = el._drag;
      if (!d || d.pointer !== e.pointerId) return;
      const dx = e.clientX - d.x, dy = e.clientY - d.y;
      if (Math.abs(dy) > 26 && Math.abs(dy) > Math.abs(dx)) {
        el._drag = null;
        el._dragged = Date.now();
        if (d.on) back(el, d);
        return;
      }
      if (!d.on) {
        if (Math.abs(dx) < 14) return;
        d.on = true;
        d.opacity = el.style.opacity;
        d.transition = el.style.transition;
        el.style.transition = "none";
        try { el.setPointerCapture(e.pointerId); } catch (_) { /* a synthesised pointer has nothing to capture */ }
      }
      d.dx = Math.max(-148, Math.min(148, dx));
      el.style.transform = "translateX(" + d.dx + "px)";
      el.style.opacity = String(1 - (Math.abs(d.dx) / 148) * 0.6);
    },
    up: (e) => {
      const el = e.currentTarget, d = el._drag;
      if (!d || d.pointer !== e.pointerId) return;
      el._drag = null;
      if (!d.on) return;
      el._dragged = Date.now();
      if (Math.abs(d.dx) < 96 || e.type === "pointercancel") { back(el, d); return; }
      if (asks) { back(el, d); gone(); return; }
      el.style.transition = "transform .2s var(--ease-sheet), opacity .2s ease";
      el.style.transform = "translateX(" + (d.dx < 0 ? -110 : 110) + "%)";
      el.style.opacity = "0";
      setTimeout(gone, 200);
    },
    dragged: (e) => {
      const el = e && e.currentTarget;
      return !!(el && el._dragged && Date.now() - el._dragged < 400);
    },
  };
};
