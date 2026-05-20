// Viewer: tabs, filtering, description toggle, workbook switch, cell popup.
(function (global) {
    'use strict';

    var popupEl = null;
    var popupContentEl = null;

    function cellFullText(td) {
        if (td.dataset && td.dataset.fullText !== undefined) {
            return td.dataset.fullText;
        }
        return td.textContent || '';
    }

    function getGlobalState() {
        var globalInput = document.getElementById('globalSearch');
        var caseInput = document.getElementById('caseSensitive');
        var caseSensitive = !!(caseInput && caseInput.checked);
        var raw = globalInput ? (globalInput.value || '').trim() : '';
        return {
            caseSensitive: caseSensitive,
            globalTerm: caseSensitive ? raw : raw.toLowerCase()
        };
    }

    function getGlobalStateForExpand() {
        var searchInput = document.getElementById('tableExpandSearch');
        var caseInput = document.getElementById('tableExpandCase');
        var caseSensitive = !!(caseInput && caseInput.checked);
        var raw = searchInput ? (searchInput.value || '').trim() : '';
        return {
            caseSensitive: caseSensitive,
            globalTerm: caseSensitive ? raw : raw.toLowerCase()
        };
    }

    function rowText(row, caseSensitive) {
        var key = caseSensitive ? '__cellsCS' : '__cellsCI';
        var joinedKey = caseSensitive ? '__joinedCS' : '__joinedCI';
        if (!row[key]) {
            var cells = row.querySelectorAll('td[data-col-idx]');
            var parts = [];
            var joinParts = [];
            cells.forEach(function (td) {
                var idx = parseInt(td.dataset.colIdx, 10);
                var t = cellFullText(td);
                var norm = caseSensitive ? t : t.toLowerCase();
                parts[idx] = norm;
                joinParts.push(norm);
            });
            row[key] = parts;
            row[joinedKey] = joinParts.join('  ');
        }
        return { cells: row[key], joined: row[joinedKey] };
    }

    function applyFiltersToTable(table, state) {
        var colFilters = [];
        table.querySelectorAll('.col-filter').forEach(function (input) {
            var raw = (input.value || '').trim();
            if (!raw) return;
            var col = parseInt(input.dataset.col, 10);
            if (!Number.isFinite(col) || col < 0) return;
            colFilters.push({
                col: col,
                term: state.caseSensitive ? raw : raw.toLowerCase()
            });
        });

        var rows = table.tBodies[0] ? table.tBodies[0].rows : [];
        var visible = 0;

        for (var r = 0; r < rows.length; r++) {
            var row = rows[r];
            var info = rowText(row, state.caseSensitive);
            var show = true;

            if (state.globalTerm && info.joined.indexOf(state.globalTerm) === -1) {
                show = false;
            }
            if (show && colFilters.length) {
                for (var i = 0; i < colFilters.length; i++) {
                    var f = colFilters[i];
                    var cellText = info.cells[f.col];
                    if (!cellText || cellText.indexOf(f.term) === -1) {
                        show = false;
                        break;
                    }
                }
            }

            row.style.display = show ? '' : 'none';
            if (show) visible++;
        }

        var visEl = table.querySelector('.row-visible');
        if (visEl) visEl.textContent = visible;

        table.querySelectorAll('.cell-display').forEach(function (el) {
            if (el.closest('tbody')) {
                var text = cellFullText(el);
                el.classList.toggle('cell-display--truncated', text.length > 0 && isTruncated(el));
            }
        });
    }

    function clearRowTextCache(root) {
        (root || document).querySelectorAll('.data-table tbody tr').forEach(function (row) {
            delete row.__cellsCS;
            delete row.__cellsCI;
            delete row.__joinedCS;
            delete row.__joinedCI;
        });
    }

    function applyAllFilters() {
        var state = getGlobalState();
        document.querySelectorAll('.sheet-panel:not(.is-hidden) table.data-table').forEach(function (t) {
            applyFiltersToTable(t, state);
        });
    }

    function activateTab(tabBtn) {
        var targetId = tabBtn.getAttribute('data-sheet-target');
        document.querySelectorAll('.tab').forEach(function (t) {
            t.classList.remove('tab--active');
            t.setAttribute('aria-selected', 'false');
        });
        tabBtn.classList.add('tab--active');
        tabBtn.setAttribute('aria-selected', 'true');

        document.querySelectorAll('.sheet-panel').forEach(function (p) {
            p.classList.toggle('is-hidden', p.id !== targetId);
        });

        applyAllFilters();
        markTruncatedCells();
        syncDescriptionToggleVisibility();
    }

    function syncDescriptionToggleVisibility() {
        var toggle = document.getElementById('showDescription');
        if (!toggle) return;
        var visiblePanel = document.querySelector('.sheet-panel:not(.is-hidden)');
        var hasFreeform = visiblePanel && visiblePanel.querySelector('[data-freeform-panel]');
        var label = toggle.closest('.description-toggle');
        if (label) {
            label.style.display = hasFreeform ? '' : 'none';
        }
    }

    function isTruncated(el) {
        return el.scrollWidth > el.clientWidth + 1
            || el.scrollHeight > el.clientHeight + 1;
    }

    function markTruncatedCells() {
        document.querySelectorAll('.cell-display').forEach(function (el) {
            var text = cellFullText(el);
            var hasText = text.length > 0;
            el.classList.toggle('cell-display--truncated', hasText && isTruncated(el));
        });
    }

    function openCellPopup(text) {
        if (!popupEl || !popupContentEl) return;
        popupContentEl.textContent = text;
        popupEl.classList.remove('is-hidden');
        if (!document.getElementById('tableExpandModal') ||
            document.getElementById('tableExpandModal').classList.contains('is-hidden')) {
            document.body.classList.add('modal-open');
        }
    }

    function closeCellPopup() {
        if (!popupEl) return;
        popupEl.classList.add('is-hidden');
        if (!document.getElementById('tableExpandModal') ||
            document.getElementById('tableExpandModal').classList.contains('is-hidden')) {
            document.body.classList.remove('modal-open');
        }
    }

    function applyDescriptionVisibility(show) {
        document.querySelectorAll('[data-freeform-panel]').forEach(function (panel) {
            panel.classList.toggle('is-hidden', !show);
        });
    }

    function bindColFilters(root) {
        (root || document).querySelectorAll('.col-filter').forEach(function (input) {
            if (input.dataset.bound === '1') return;
            input.dataset.bound = '1';
            input.addEventListener('input', function () {
                var table = input.closest('table.data-table');
                if (!table) return;
                var inExpand = table.closest('#tableExpandBody');
                applyFiltersToTable(table, inExpand ? getGlobalStateForExpand() : getGlobalState());
            });
        });
    }

    global.MappingViewer = {
        getGlobalState: getGlobalState,
        getGlobalStateForExpand: getGlobalStateForExpand,
        applyFiltersToTable: applyFiltersToTable,
        applyAllFilters: applyAllFilters,
        markTruncatedCells: markTruncatedCells,
        clearRowTextCache: clearRowTextCache
    };

    document.addEventListener('DOMContentLoaded', function () {
        popupEl = document.getElementById('cellPopup');
        popupContentEl = document.getElementById('cellPopupContent');

        if (popupEl) {
            popupEl.querySelectorAll('[data-close-popup]').forEach(function (el) {
                el.addEventListener('click', closeCellPopup);
            });
        }

        var tableExpandModal = document.getElementById('tableExpandModal');
        if (tableExpandModal) {
            tableExpandModal.querySelectorAll('[data-close-table-expand]').forEach(function (el) {
                el.addEventListener('click', function () {
                    if (global.TableGrid) global.TableGrid.closeExpand();
                });
            });
        }

        document.addEventListener('keydown', function (e) {
            if (e.key !== 'Escape') return;
            if (tableExpandModal && !tableExpandModal.classList.contains('is-hidden')) {
                if (global.TableGrid) global.TableGrid.closeExpand();
                return;
            }
            if (popupEl && !popupEl.classList.contains('is-hidden')) {
                closeCellPopup();
            }
        });

        document.addEventListener('click', function (e) {
            if (e.target.closest('.col-resize-handle') || e.target.closest('[data-expand-table]') ||
                e.target.closest('[data-reset-columns]')) {
                return;
            }
            var cell = e.target.closest('.cell-display--truncated');
            if (!cell) return;
            openCellPopup(cellFullText(cell));
        });

        document.addEventListener('click', function (e) {
            var expandBtn = e.target.closest('[data-expand-table]');
            if (expandBtn) {
                var block = expandBtn.closest('.table-block');
                var table = block ? block.querySelector('table.data-table') : null;
                if (table && global.TableGrid) global.TableGrid.openExpand(table);
                return;
            }
            var resetBtn = e.target.closest('[data-reset-columns]');
            if (resetBtn) {
                var block2 = resetBtn.closest('.table-block');
                var table2 = block2 ? block2.querySelector('table.data-table') : null;
                if (table2 && global.TableGrid) {
                    global.TableGrid.reset(table2);
                    clearRowTextCache(table2);
                    applyAllFilters();
                    markTruncatedCells();
                }
            }
        });

        var resizeTimer;
        window.addEventListener('resize', function () {
            clearTimeout(resizeTimer);
            resizeTimer = setTimeout(markTruncatedCells, 150);
        });

        if (global.TableGrid) global.TableGrid.initAll();
        bindColFilters();
        markTruncatedCells();

        var workbookSelect = document.getElementById('workbookSelect');
        if (workbookSelect) {
            workbookSelect.addEventListener('change', function () {
                var file = workbookSelect.value;
                if (!file) return;
                window.location.href = '/Home/Viewer?file=' + encodeURIComponent(file);
            });
        }

        var showDescription = document.getElementById('showDescription');
        var descKey = 'mappingViewer.showDescription';
        if (showDescription) {
            var saved = localStorage.getItem(descKey);
            if (saved === 'false') showDescription.checked = false;
            applyDescriptionVisibility(showDescription.checked);
            showDescription.addEventListener('change', function () {
                localStorage.setItem(descKey, showDescription.checked ? 'true' : 'false');
                applyDescriptionVisibility(showDescription.checked);
                markTruncatedCells();
            });
            syncDescriptionToggleVisibility();
        }

        document.querySelectorAll('.tab').forEach(function (btn) {
            btn.addEventListener('click', function () { activateTab(btn); });
        });

        var globalInput = document.getElementById('globalSearch');
        if (globalInput) {
            globalInput.addEventListener('input', function () {
                clearRowTextCache();
                applyAllFilters();
            });
        }

        var caseInput = document.getElementById('caseSensitive');
        if (caseInput) {
            caseInput.addEventListener('change', function () {
                clearRowTextCache();
                applyAllFilters();
            });
        }
    });
})(window);
