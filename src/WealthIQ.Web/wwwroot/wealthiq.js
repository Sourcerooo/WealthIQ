// Midnight Ledger — progressive enhancement. No framework; safe if it no-ops.
window.wealthiq = {
    // Theme persistence (called from ThemePreferenceService via JS interop).
    getTheme: function () {
        try { return localStorage.getItem('wiq-theme'); } catch { return null; }
    },
    setTheme: function (value) {
        try { localStorage.setItem('wiq-theme', value); } catch { /* ignore */ }
    },

    // Animate every element matching `.wiq-countup[data-target]` from 0 to its target.
    // The element's static text is already the correct final value, so this is purely cosmetic.
    runCountUps: function () {
        var reduce = window.matchMedia && window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        var els = document.querySelectorAll('.wiq-countup[data-target]');
        els.forEach(function (el) {
            if (el.dataset.wiqDone === '1') return;
            el.dataset.wiqDone = '1';
            var target = parseFloat(el.dataset.target);
            if (isNaN(target)) return;
            var suffix = el.dataset.suffix || '';
            var fmt = new Intl.NumberFormat('de-DE', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
            if (reduce) { el.textContent = fmt.format(target) + suffix; return; }
            var start = performance.now();
            var dur = 900;
            function frame(now) {
                var t = Math.min(1, (now - start) / dur);
                var eased = 1 - Math.pow(1 - t, 3);
                el.textContent = fmt.format(target * eased) + suffix;
                if (t < 1) requestAnimationFrame(frame);
            }
            requestAnimationFrame(frame);
        });
    }
};
