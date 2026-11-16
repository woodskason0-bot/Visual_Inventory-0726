// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

// Write your JavaScript code.

// App shell: sidebar collapse (desktop) and off-canvas drawer (mobile). Collapse state lives in localStorage.
// Waits for DOMContentLoaded: #appSidebar renders after this script loads.
document.addEventListener('DOMContentLoaded', function () {
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

    // Fixed-position dashboard grids ignore the flex layout, so they read the sidebar width from this variable.
    function applySidebarWidthVar(collapsed) {
        document.documentElement.style.setProperty('--vis-sidebar-width', collapsed ? '68px' : '240px');
    }

    var startCollapsed = localStorage.getItem('vis-sidebar-collapsed') === '1';
    if (startCollapsed) {
        sidebar.classList.add('collapsed');
        applyCollapsedUI(true);
    }
    applySidebarWidthVar(startCollapsed);

    if (collapseBtn) {
        collapseBtn.addEventListener('click', function () {
            var collapsed = sidebar.classList.toggle('collapsed');
            localStorage.setItem('vis-sidebar-collapsed', collapsed ? '1' : '0');
            applyCollapsedUI(collapsed);
            applySidebarWidthVar(collapsed);
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
});

// Shared modal helpers. They read page-level data (itemsList, orgStructure, teamLines, rackRowMap,
// locDecodeMap, locMap) by name at call time, so each calling page must declare those.
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
    // For the big template literals that wrap a database name in HTML. Prefer createElement + textContent.
    function escapeHtml(value) {
        return String(value == null ? '' : value)
            .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }
    // Branch -> Line cascade, from one vocabulary (orgStructure), for every paired Branch and Line select.
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
    // Wires a Branch select to its Line select. Call once per pair.
    // onChange fires after either select changes. The placeholders set each caller's wording.
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
    // Sets both selects to a known Line. Silent: does not fire onChange.
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
    // selectable = false: matches are a heads-up only (New Item Registry).
    // jumpToStock = true: clicking a match closes New Item and opens Modify Stock on it.
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
                        btn.className = 'list-group-item list-group-item-action py-2 px-3 small border-bottom vis-suggest-item';
                        // Built from nodes, not an HTML string: names and part numbers are user-typed.
                        const idEl = document.createElement('strong');
                        idEl.className = 'text-primary';
                        idEl.textContent = m.id;
                        btn.appendChild(idEl);
                        if (m.rpn) {
                            const rpnEl = document.createElement('span');
                            rpnEl.className = 'text-info';
                            rpnEl.textContent = '[' + m.rpn + ']';
                            btn.appendChild(document.createTextNode(' '));
                            btn.appendChild(rpnEl);
                        }
                        btn.appendChild(document.createTextNode(' - ' + m.name + ' '));
                        const qtyEl = document.createElement('span');
                        qtyEl.className = 'text-light-gray float-end';
                        qtyEl.textContent = 'Qty: ' + m.quantity;
                        btn.appendChild(qtyEl);

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
    // Autocomplete over a plain list of strings, or a function returning one (re-read on every keystroke).
    // opts.floating pins the list to the viewport; opts.extraItem adds one row after the matches.
    function bindValueAutocomplete(inputEl, listEl, valuesOrGetter, opts) {
        if (!inputEl || !listEl) return;
        opts = opts || {};
        let suppress = false;

        function place() {
            if (!opts.floating || listEl.style.display === 'none') return;
            const r = inputEl.getBoundingClientRect();
            listEl.style.position = 'fixed';
            listEl.style.left = r.left + 'px';
            listEl.style.minWidth = r.width + 'px';
            const h = listEl.offsetHeight;
            const below = window.innerHeight - r.bottom;
            listEl.style.top = ((below < h + 4 && r.top > h + 4) ? (r.top - h - 2) : (r.bottom + 2)) + 'px';
        }
        if (opts.floating) {
            // Capture phase: also catches the table wrapper's own horizontal scroll.
            window.addEventListener('scroll', place, true);
            window.addEventListener('resize', place);
        }

        function row(label, onPick, extraClass) {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'list-group-item list-group-item-action py-2 px-3 small border-bottom vis-suggest-item' + (extraClass ? ' ' + extraClass : '');
            btn.textContent = label;
            btn.onclick = function () { listEl.style.display = 'none'; onPick(); };
            listEl.appendChild(btn);
        }

        inputEl.addEventListener('input', function () {
            if (suppress) { suppress = false; return; }
            const raw = inputEl.value.trim();
            const val = raw.toLowerCase();
            listEl.innerHTML = '';
            if (!val) { listEl.style.display = 'none'; return; }
            const values = (typeof valuesOrGetter === 'function') ? (valuesOrGetter() || []) : valuesOrGetter;
            const matches = values.filter(function (v) { return v.toLowerCase().includes(val); }).slice(0, 10);
            const extra = opts.extraItem ? opts.extraItem(raw, matches) : null;
            if (matches.length === 0 && !extra) { listEl.style.display = 'none'; return; }
            matches.forEach(function (v) {
                row(v, function () {
                    suppress = true;
                    inputEl.value = v;
                    inputEl.dispatchEvent(new Event('input', { bubbles: true }));
                });
            });
            if (extra) row(extra.label, extra.onPick, 'fst-italic text-info');
            listEl.style.display = 'block';
            place();
        });
        document.addEventListener('click', function (e) {
            if (e.target !== inputEl) listEl.style.display = 'none';
        });
    }
    // Client half of the location code rule: 1st, 3rd, 5th and last letter or digit.
    // Keep in step with Services/LocationCodec.Encode().
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

            // Rack/Row suggestions, re-read on every keystroke for the current Parent/Major/Sub.
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
