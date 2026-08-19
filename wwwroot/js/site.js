// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// App shell: sidebar collapse (desktop) + off-canvas drawer (mobile).
// Collapse state is client-side only (localStorage), not a User field --
// a UI preference, not worth a schema change yet.
(function () {
    var sidebar = document.getElementById('appSidebar');
    if (!sidebar) return;

    var backdrop = document.getElementById('sidebarBackdrop');
    var collapseBtn = document.getElementById('sidebarCollapseBtn');
    var collapseIcon = document.getElementById('sidebarCollapseIcon');
    var collapseLabel = document.getElementById('sidebarCollapseLabel');
    var hamburger = document.getElementById('sidebarHamburger');

    function applyCollapsedUI(collapsed) {
        if (collapseIcon) collapseIcon.className = collapsed ? 'bi bi-chevron-double-right' : 'bi bi-chevron-double-left';
        if (collapseLabel) collapseLabel.textContent = collapsed ? 'Expand' : 'Collapse';
    }

    var startCollapsed = localStorage.getItem('vis-sidebar-collapsed') === '1';
    if (startCollapsed) {
        sidebar.classList.add('collapsed');
        applyCollapsedUI(true);
    }

    if (collapseBtn) {
        collapseBtn.addEventListener('click', function () {
            var collapsed = sidebar.classList.toggle('collapsed');
            localStorage.setItem('vis-sidebar-collapsed', collapsed ? '1' : '0');
            applyCollapsedUI(collapsed);
        });
    }

    function openMobileSidebar() {
        sidebar.classList.add('mobile-open');
        if (backdrop) backdrop.classList.add('show');
    }
    function closeMobileSidebar() {
        sidebar.classList.remove('mobile-open');
        if (backdrop) backdrop.classList.remove('show');
    }

    if (hamburger) hamburger.addEventListener('click', openMobileSidebar);
    if (backdrop) backdrop.addEventListener('click', closeMobileSidebar);
    sidebar.querySelectorAll('.sidebar-link').forEach(function (a) {
        a.addEventListener('click', closeMobileSidebar);
    });
})();

// ---------------------------------------------------------------------------
// Pass 28 (2a): generic dashboard-modal utilities promoted here from
// Views/Home/Index.cshtml's inline script, so the Modify Stock / New Item
// Registry / Alert Rules partials (and, later, Command Center + Search
// Center) can all call them without each page re-declaring its own copy.
// Every function here operates on page-level data consts (itemsList,
// orgStructure, teamLines, rackRowMap, locDecodeMap, locMap) that each
// calling page must still declare itself -- these are resolved by name at
// CALL time, not at parse time, so it's safe for this file to load before
// those consts exist (same cross-script global-scope rule Pass 27's
// load-order fix already established for site.js as a whole).
// ---------------------------------------------------------------------------
    function racksRowsFor(pCode, mCode, sCode) {
        const key = (pCode || '') + '|' + (mCode || '') + '|' + (sCode || '');
        return rackRowMap[key] || { racks: [], rows: [] };
    }
    function executeSubmit(id, btnElement) {
        const f = document.getElementById(id);
        if (f) {
            if (btnElement) {
                btnElement.disabled = true;
                btnElement.innerHTML = '<span class="spinner-border spinner-border-sm"></span> Processing...';
            }
            f.submit();
        }
    }
    // ---- Shared Branch -> Line cascade ---------------------------------
    // One vocabulary (orgStructure), reused everywhere a Branch select drives
    // a paired Line select, instead of each caller hand-rolling its own copy.
    // That's the exact failure shape the location vocabulary hit eight times
    // over (the same small piece of org data re-typed in multiple places,
    // silently drifting) -- same risk here at smaller scale. Three callers as
    // of this pass: the Ownership pane (edit-line-branch/edit-line), New Item
    // Registry (reg-line-branch/reg-line), and the compressor filter
    // (compFilterBranch/compFilterLine).
    function branchForLine(line) {
        for (var branch in orgStructure) {
            if (orgStructure[branch].indexOf(line) !== -1) return branch;
        }
        return "";
    }
    function populateBranchSelect(branchSelect, placeholder) {
        if (!branchSelect) return;
        branchSelect.innerHTML = '<option value="">' + (placeholder || '-- Branch --') + '</option>';
        Object.keys(orgStructure).forEach(function (branch) {
            const opt = document.createElement('option');
            opt.value = branch;
            opt.textContent = branch;
            branchSelect.appendChild(opt);
        });
    }
    function populateLineSelect(lineSelect, branch, selectedLine, placeholder) {
        if (!lineSelect) return;
        lineSelect.innerHTML = '<option value="">' + (placeholder || 'Unassigned') + '</option>';
        const lines = orgStructure[branch] || [];
        lines.forEach(function (line) {
            const opt = document.createElement('option');
            opt.value = line;
            opt.textContent = line;
            if (line === selectedLine) opt.selected = true;
            lineSelect.appendChild(opt);
        });
    }
    // Wires a Branch select + its paired Line select into a live cascade:
    // populates both immediately, and on Branch change repopulates Line's
    // options to just that branch's lines. Call once per pair, at setup time.
    // `onChange`, if given, fires after EITHER select changes (after the
    // cascade has already updated) -- e.g. to refresh a live ID preview or
    // re-apply a filter. `branchPlaceholder`/`linePlaceholder` let each
    // caller use its own wording ("Unassigned" vs "All Lines").
    function wireLineCascade(branchSelect, lineSelect, onChange, branchPlaceholder, linePlaceholder) {
        if (!branchSelect || !lineSelect) return;
        populateBranchSelect(branchSelect, branchPlaceholder);
        populateLineSelect(lineSelect, branchSelect.value, '', linePlaceholder);
        branchSelect.addEventListener('change', function () {
            populateLineSelect(lineSelect, branchSelect.value, '', linePlaceholder);
            if (onChange) onChange();
        });
        if (onChange) lineSelect.addEventListener('change', onChange);
    }
    // Seeds both selects to reflect a KNOWN current Line (e.g. the item's
    // existing Line when the Ownership pane opens, or a Team's assigned
    // Line when auto-filling). Silent -- does not fire onChange; callers
    // that need a reaction call it themselves afterward if they want one.
    function setLineCascade(branchSelect, lineSelect, currentLine, branchPlaceholder, linePlaceholder) {
        if (!branchSelect || !lineSelect) return;
        populateBranchSelect(branchSelect, branchPlaceholder);
        const branch = branchForLine(currentLine || '');
        if (branch) branchSelect.value = branch;
        populateLineSelect(lineSelect, branch, currentLine || '', linePlaceholder);
    }
    function locDecode(code) {
        return (code != null && locDecodeMap && locDecodeMap[code]) ? locDecodeMap[code] : code;
    }
    // selectable = false is the New Item Registry case: matches show as a
    // heads-up ("something similar may already exist"), but there's no
    // idOutputId to populate and clicking one never gates or blocks typing a
    // brand new name -- registering something NEW is the whole point of that
    // form, unlike Modify Stock/Alerts where picking an existing item is
    // required for the rest of the pane to function.
    //
    // jumpToStock = true is the same New Item Registry case taken one step
    // further: clicking a match means "this already exists, take me there" --
    // closes the New Item modal and opens Modify Stock preloaded with it,
    // discarding whatever was typed. Only meaningful alongside
    // selectable = false (there's nothing for the registry form itself to do
    // with the pick).
    function bindAutocomplete(inputId, listId, idOutputId, isAlertModal, selectable = true, jumpToStock = false) {
        const searchInput = document.getElementById(inputId);
        const autoList = document.getElementById(listId);
        const idInput = idOutputId ? document.getElementById(idOutputId) : null;

        if (searchInput) {
            searchInput.addEventListener('input', function() {
                let val = this.value.toLowerCase();
                autoList.innerHTML = '';
                if (!val) { autoList.style.display = 'none'; return; }

                let matches = itemsList.filter(i =>
                    i.id.toLowerCase().includes(val) || i.name.toLowerCase().includes(val)
                    || (i.rpn && i.rpn.toLowerCase().includes(val))
                ).slice(0, 10);

                if (matches.length > 0) {
                    autoList.style.display = 'block';
                    matches.forEach(m => {
                        let btn = document.createElement('button');
                        btn.type = 'button';
                        btn.className = 'list-group-item list-group-item-action py-2 px-3 small border-bottom border-dark text-white';
                        btn.style.backgroundColor = '#1C1E24';
                        btn.innerHTML = `<strong class="text-primary">${m.id}</strong>${m.rpn ? ' <span class="text-info">[' + m.rpn + ']</span>' : ''} - ${m.name} <span class="text-light-gray float-end">Qty: ${m.quantity}</span>`;

                        btn.onclick = function() {
                            autoList.style.display = 'none';
                            if (jumpToStock) {
                                const modalEl = document.getElementById('newItemModal');
                                const inst = modalEl ? bootstrap.Modal.getInstance(modalEl) : null;
                                if (inst) {
                                    modalEl.addEventListener('hidden.bs.modal', function h() {
                                        modalEl.removeEventListener('hidden.bs.modal', h);
                                        handleStock(m.id);
                                    });
                                    inst.hide();
                                } else {
                                    handleStock(m.id);
                                }
                                return;
                            }
                            if (!selectable) return; // informational only -- don't touch the typed name

                            searchInput.value = m.name;
                            idInput.value = m.id;

                            if (isAlertModal) {
                                document.getElementById('alert-threshold').value = m.threshold;
                            } else if (typeof toggleStockUI === 'function') {
                                // Rebuild the variant selector + re-seed the cascade
                                // for the newly picked item, whatever the action.
                                toggleStockUI();
                            }
                        };

                        btn.onmouseover = () => btn.style.backgroundColor = '#2A2D35';
                        btn.onmouseout = () => btn.style.backgroundColor = '#1C1E24';

                        autoList.appendChild(btn);
                    });
                } else {
                    autoList.style.display = 'none';
                }
            });

            document.addEventListener('click', function(e) {
                if (e.target !== searchInput) autoList.style.display = 'none';
            });
        }
    }
    // Generic single-value autocomplete (Type fields): a plain list of
    // strings, no id/qty metadata like bindAutocomplete's item matches carry.
    // Same list-group visual pattern as bindAutocomplete, so every dropdown
    // in the app looks the same. Picking a suggestion sets the input's value
    // and fires a real 'input' event so whatever's already listening (the ID
    // preview, unit-capture toggle) still runs -- suppressed once so that
    // synthetic event doesn't reopen this same list.
    // valuesOrGetter: a plain array (static, e.g. known Types) OR a function
    // returning the current array (dynamic, e.g. Rack/Row suggestions that
    // depend on whatever Parent/Major/Sub is picked right now) -- evaluated
    // fresh on every keystroke either way, so a dynamic source never goes stale.
    function bindValueAutocomplete(inputEl, listEl, valuesOrGetter) {
        if (!inputEl || !listEl) return;
        let suppress = false;
        inputEl.addEventListener('input', function () {
            if (suppress) { suppress = false; return; }
            const val = inputEl.value.trim().toLowerCase();
            listEl.innerHTML = '';
            if (!val) { listEl.style.display = 'none'; return; }
            const values = (typeof valuesOrGetter === 'function') ? (valuesOrGetter() || []) : valuesOrGetter;
            const matches = values.filter(function (v) { return v.toLowerCase().includes(val); }).slice(0, 10);
            if (matches.length === 0) { listEl.style.display = 'none'; return; }
            listEl.style.display = 'block';
            matches.forEach(function (v) {
                const btn = document.createElement('button');
                btn.type = 'button';
                btn.className = 'list-group-item list-group-item-action py-2 px-3 small border-bottom border-dark text-white';
                btn.style.backgroundColor = '#1C1E24';
                btn.textContent = v;
                btn.onmouseover = function () { btn.style.backgroundColor = '#2A2D35'; };
                btn.onmouseout = function () { btn.style.backgroundColor = '#1C1E24'; };
                btn.onclick = function () {
                    listEl.style.display = 'none';
                    suppress = true;
                    inputEl.value = v;
                    inputEl.dispatchEvent(new Event('input', { bubbles: true }));
                };
                listEl.appendChild(btn);
            });
        });
        document.addEventListener('click', function (e) {
            if (e.target !== inputEl) listEl.style.display = 'none';
        });
    }
    // Client-side half of the location code rule: 1st, 3rd, 5th and LAST
    // letter/digit of the cleaned name. Must stay in lockstep with
    // Services/LocationCodec.Encode().
    //
    // NOTE: duplicated in Views/Home/Intake.cshtml. Moving it to wwwroot/js/site.js
    // was tried and reverted -- _Layout loads site.js after the body renders, so a
    // view's inline script runs before it would exist. Fixing the duplication means
    // moving that script reference higher in the layout, which touches every page.
    //
    // And do NOT write the layout's body-render call by name in here with a leading
    // at-sign: inside a .cshtml file that is a Razor transition even within a JS
    // comment, and it will be invoked for real. That is exactly how this comment
    // took the dashboard down once already.
    function encodeLoc(name) {
        if (!name) return "";
        const clean = String(name).toUpperCase().replace(/[^A-Z0-9]/g, "");
        if (!clean.length) return "";
        const n = clean.length;
        const pos = [...new Set([1, 3, 5, n].filter(p => p >= 1 && p <= n))].sort((a, b) => a - b);
        return pos.map(p => clean[p - 1]).join("");
    }
        function bindCascadingLocation(pId, mId, sId, rkId, rwId, fdaId, rkListId, rwListId) {
            const pSel = document.getElementById(pId);
            const mSel = document.getElementById(mId);
            const sSel = document.getElementById(sId);
            const rkIn = document.getElementById(rkId);
            const rwIn = document.getElementById(rwId);
            const fdaOut = document.getElementById(fdaId);

            // Rack/Row suggestions, re-evaluated on every keystroke against
            // whatever Parent/Major/Sub is picked right now (bindValueAutocomplete
            // takes a getter, not a fixed list, for exactly this).
            if (rkIn && rkListId) {
                bindValueAutocomplete(rkIn, document.getElementById(rkListId), function () {
                    return racksRowsFor(encodeLoc(pSel ? pSel.value : ''), encodeLoc(mSel ? mSel.value : ''), encodeLoc(sSel ? sSel.value : '')).racks;
                });
            }
            if (rwIn && rwListId) {
                bindValueAutocomplete(rwIn, document.getElementById(rwListId), function () {
                    return racksRowsFor(encodeLoc(pSel ? pSel.value : ''), encodeLoc(mSel ? mSel.value : ''), encodeLoc(sSel ? sSel.value : '')).rows;
                });
            }

            function updateFda() {
                if(!pSel || !fdaOut) return;

                let pCode = encodeLoc(pSel.value);
                let mCode = encodeLoc(mSel.value);
                let sCode = encodeLoc(sSel.value);

                let rkCode = rkIn ? rkIn.value.trim().toUpperCase() : "";
                let rwCode = rwIn ? rwIn.value.trim() : "";

                let parts = [pCode, mCode, sCode, rkCode, rwCode].filter(x => x !== "");
                fdaOut.value = parts.join('.');
            }

            if (pSel) {
                pSel.addEventListener('change', function() {
                    mSel.innerHTML = '<option value="">Select Major...</option>';
                    sSel.innerHTML = '<option value="">Select Sub...</option>';
                    sSel.disabled = true;

                    let pName = this.value;
                    let majors = (pName && locMap[pName]) ? Object.keys(locMap[pName]) : [];
                    if (majors.length > 0) {
                        mSel.disabled = false;
                        majors.forEach(m => mSel.add(new Option(m, m)));
                    } else {
                        mSel.disabled = true;
                    }
                    updateFda();
                });

                mSel.addEventListener('change', function() {
                    sSel.innerHTML = '<option value="">Select Sub...</option>';
                    let pName = pSel.value;
                    let mName = this.value;
                    let subs = (pName && mName && locMap[pName] && locMap[pName][mName]) ? locMap[pName][mName] : [];

                    if (subs.length > 0) {
                        sSel.disabled = false;
                        subs.forEach(s => sSel.add(new Option(s, s)));
                    } else {
                        sSel.disabled = true;
                    }
                    updateFda();
                });

                sSel.addEventListener('change', updateFda);
                if(rkIn) rkIn.addEventListener('input', updateFda);
                if(rwIn) rwIn.addEventListener('input', updateFda);
            }
        }
